// SPDX-License-Identifier: GPL-3.0-or-later
//
// Thin C ABI around akustikrausch/airplay2-sender-cpp.
// The C# application feeds interleaved s16 stereo PCM at 44.1 kHz into a
// lock-free ring buffer; the native loop owns RTSP/RTP and the sender state.

#include "airplay_core.h"

#include "raop_loop.h"
#include "raop_sender.h"
#include "ring_buffer.h"

#include <algorithm>
#include <atomic>
#include <chrono>
#include <cstring>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

using namespace fxchain;

struct ap_handle {
    std::string name;
    std::string device_id;
    std::string model;
    int sample_rate = 44100;
    int channels = 2;

    ap_state_cb state_cb = nullptr;
    ap_log_cb log_cb = nullptr;
    void* user = nullptr;

    std::atomic<int> state{AP_STATE_IDLE};
    std::atomic<bool> stop{false};
    std::atomic<unsigned long long> dropped_frames{0};
    std::atomic<double> pending_volume{50.0};

    std::mutex mutex;
    std::deque<std::function<void()>> commands;
    std::unique_ptr<RaopLoop> loop;
    std::unique_ptr<RaopSender> sender;
    std::unique_ptr<RingBuffer<int16_t>> ring;
    std::thread thread;
    std::string last_error;
};

namespace {

void set_state(ap_handle* h, int state, const std::string& detail) {
    if (!h) return;
    h->state.store(state, std::memory_order_release);
    if (!detail.empty()) {
        std::lock_guard<std::mutex> lock(h->mutex);
        h->last_error = detail;
    }
    if (h->state_cb) {
        h->state_cb(state, detail.empty() ? nullptr : detail.c_str(), h->user);
    }
}

void log_line(ap_handle* h, int level, const std::string& line) {
    if (h && h->log_cb) h->log_cb(level, line.c_str(), h->user);
}

void enqueue(ap_handle* h, std::function<void()> fn) {
    std::lock_guard<std::mutex> lock(h->mutex);
    h->commands.push_back(std::move(fn));
}

void drain_commands(ap_handle* h) {
    std::deque<std::function<void()>> local;
    {
        std::lock_guard<std::mutex> lock(h->mutex);
        local.swap(h->commands);
    }
    for (auto& fn : local) {
        try {
            fn();
        } catch (const std::exception& e) {
            set_state(h, AP_STATE_ERROR, std::string("Command failed: ") + e.what());
            h->stop.store(true);
            if (h->loop) h->loop->requestStop();
        }
    }
}

void run_loop(ap_handle* h) {
    log_line(h, 0, "native: loop thread started");
    while (!h->stop.load(std::memory_order_acquire)) {
        drain_commands(h);
        if (h->stop.load(std::memory_order_acquire)) break;
        if (!h->loop || !h->sender) {
            log_line(h, 1, "native: loop missing sender/loop");
            break;
        }
        try {
            h->loop->pump(*h->sender, std::chrono::milliseconds(20));
        } catch (const std::exception& e) {
            set_state(h, AP_STATE_ERROR, std::string("Network loop failed: ") + e.what());
            h->stop.store(true);
            break;
        }
    }
    log_line(h, 0, "native: loop thread exited");
    if (h->state.load() != AP_STATE_ERROR && h->state.load() != AP_STATE_STOPPED) {
        set_state(h, AP_STATE_STOPPED, "Session stopped.");
    }
}

} // namespace

extern "C" {

AP_API ap_handle* ap_create(const ap_config* config,
                            ap_state_cb state_cb,
                            ap_log_cb log_cb,
                            void* user) {
    if (!config || !config->device_name) return nullptr;
    auto* h = new (std::nothrow) ap_handle();
    if (!h) return nullptr;

    h->name = config->device_name;
    h->device_id = config->device_id ? config->device_id : "02:41:50:4C:41:59";
    h->model = config->model ? config->model : "Windows";
    h->sample_rate = config->sample_rate > 0 ? config->sample_rate : 44100;
    h->channels = config->channels > 0 ? config->channels : 2;
    h->state_cb = state_cb;
    h->log_cb = log_cb;
    h->user = user;
    return h;
}

AP_API int ap_start(ap_handle* h,
                    const char* host,
                    unsigned short port,
                    const char* password) {
    if (!h || !host || !*host) return -1;

    {
        std::lock_guard<std::mutex> lock(h->mutex);
        if (h->thread.joinable()) return -1;
        h->commands.clear();
        h->stop.store(false, std::memory_order_release);
        h->dropped_frames.store(0, std::memory_order_release);
        h->state.store(AP_STATE_CONNECTING, std::memory_order_release);
        h->last_error.clear();
        h->loop = std::make_unique<RaopLoop>();
        h->ring = std::make_unique<RingBuffer<int16_t>>(1u << 19); // ~5.9 s stereo
    }

    // Build the sender. It is safe to construct it without the command lock;
    // no callbacks fire until start() is called.
    RaopEvents events;
    events.launched = [h](bool ok, const std::string& error) {
        if (ok) {
            set_state(h, AP_STATE_STREAMING, "Streaming.");
        } else {
            set_state(h, AP_STATE_ERROR, error.empty() ? "Receiver refused the session." : error);
            h->stop.store(true, std::memory_order_release);
            if (h->loop) h->loop->requestStop();
        }
    };
    events.closed = [h]() {
        // The sender calls this when the receiver ends the session; our own
        // stop() is silent. Treat it as a stopped session for the UI.
        set_state(h, AP_STATE_STOPPED, "Session closed by the receiver.");
        h->stop.store(true, std::memory_order_release);
        if (h->loop) h->loop->requestStop();
    };
    events.pinRequired = [h](const std::string&) {
        set_state(h, AP_STATE_ERROR, "This receiver requires PIN pairing; AP1 mode does not support it.");
    };
    events.credentialsObtained = [](const std::string&, const std::string&) {};

    RaopLogSink log = [h](RaopLogLevel level, const std::string& line) {
        log_line(h, level == RaopLogLevel::Warn ? 1 : 0, line);
    };

    {
        std::lock_guard<std::mutex> lock(h->mutex);
        h->sender = std::make_unique<RaopSender>(*h->loop, std::move(events), std::move(log));
        h->sender->setInputFormat(static_cast<uint32_t>(h->sample_rate));
        h->sender->setAp1Alac(true);
        h->sender->attachRing(h->ring.get());
        h->sender->setVolume(h->pending_volume.load());
        const std::string pwd = password ? password : "";
        const auto auth = pwd.empty() ? RaopDeviceInfo::Auth::None : RaopDeviceInfo::Auth::Password;
        h->sender->setAuth(auth, /*airplay2=*/false, h->device_id, std::string(), pwd);
        RaopIdentity identity;
        identity.name = h->name;
        identity.deviceId = h->device_id;
        identity.model = h->model;
        h->sender->setIdentity(identity);
    }

    set_state(h, AP_STATE_CONNECTING, std::string("Connecting to ") + host + "...");
    try {
        h->sender->start(host, port, h->name);
    } catch (const std::exception& e) {
        set_state(h, AP_STATE_ERROR, std::string("Could not start session: ") + e.what());
        return -1;
    }

    log_line(h, 0, "native: starting loop thread");
    h->thread = std::thread([h] { run_loop(h); });
    return 0;
}

AP_API int ap_push_pcm(ap_handle* h,
                       const short* interleaved,
                       unsigned int frames) {
    if (!h || !interleaved || frames == 0) return 0;
    std::lock_guard<std::mutex> lock(h->mutex);
    if (!h->ring) return -1;
    const auto samples = static_cast<size_t>(frames) * 2u;
    if (!h->ring->tryPush(std::span<const int16_t>(interleaved, samples))) {
        h->dropped_frames.fetch_add(frames, std::memory_order_relaxed);
        return 0;
    }
    return 1;
}

AP_API int ap_set_volume(ap_handle* h, double percent) {
    if (!h) return -1;
    h->pending_volume.store(std::clamp(percent, 0.0, 100.0));
    enqueue(h, [h, percent] {
        if (h->sender) h->sender->setVolume(std::clamp(percent, 0.0, 100.0));
    });
    return 0;
}

AP_API int ap_stop(ap_handle* h) {
    if (!h) return -1;
    bool joinable = false;
    {
        std::lock_guard<std::mutex> lock(h->mutex);
        joinable = h->thread.joinable();
    }
    if (!joinable) {
        std::lock_guard<std::mutex> lock(h->mutex);
        h->loop.reset();
        h->sender.reset();
        h->ring.reset();
        h->state.store(AP_STATE_STOPPED, std::memory_order_release);
        return 0;
    }

    enqueue(h, [h] {
        if (h->sender) h->sender->stop();
        h->stop.store(true, std::memory_order_release);
        if (h->loop) h->loop->requestStop();
    });
    if (h->thread.joinable()) h->thread.join();

    {
        std::lock_guard<std::mutex> lock(h->mutex);
        h->sender.reset();
        h->loop.reset();
        h->ring.reset();
        h->state.store(AP_STATE_STOPPED, std::memory_order_release);
    }
    return 0;
}

AP_API void ap_destroy(ap_handle* h) {
    if (!h) return;
    ap_stop(h);
    delete h;
}

AP_API int ap_get_state(ap_handle* h) {
    return h ? h->state.load(std::memory_order_acquire) : AP_STATE_IDLE;
}

AP_API unsigned int ap_get_buffer_frames(ap_handle* h) {
    if (!h) return 0;
    std::lock_guard<std::mutex> lock(h->mutex);
    return h->ring ? static_cast<unsigned int>(h->ring->availableRead() / 2) : 0;
}

AP_API unsigned long long ap_get_dropped_frames(ap_handle* h) {
    return h ? h->dropped_frames.load(std::memory_order_relaxed) : 0;
}

AP_API const char* ap_last_error(ap_handle* h) {
    thread_local std::string copy;
    copy.clear();
    if (h) {
        std::lock_guard<std::mutex> lock(h->mutex);
        copy = h->last_error;
    }
    return copy.c_str();
}

} // extern "C"
