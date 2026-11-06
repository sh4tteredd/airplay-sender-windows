// SPDX-License-Identifier: GPL-3.0-or-later
#pragma once

#ifdef _WIN32
#  ifdef AIRPLAYCORE_EXPORTS
#    define AP_API __declspec(dllexport)
#  else
#    define AP_API __declspec(dllimport)
#  endif
#else
#  define AP_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef enum ap_state {
    AP_STATE_IDLE = 0,
    AP_STATE_CONNECTING = 1,
    AP_STATE_AUTHENTICATING = 2,
    AP_STATE_STREAMING = 3,
    AP_STATE_DEGRADED = 4,
    AP_STATE_RECONNECTING = 5,
    AP_STATE_ERROR = 6,
    AP_STATE_STOPPED = 7
} ap_state;

typedef void (__cdecl *ap_state_cb)(int state, const char* detail, void* user);
typedef void (__cdecl *ap_log_cb)(int level, const char* message, void* user);

typedef struct ap_config {
    const char* device_name;
    const char* device_id;
    const char* model;
    int sample_rate;  /* 44100 */
    int channels;     /* 2 */
} ap_config;

typedef struct ap_handle ap_handle;

AP_API ap_handle* ap_create(const ap_config* config,
                            ap_state_cb state_cb,
                            ap_log_cb log_cb,
                            void* user);
AP_API int ap_start(ap_handle* handle,
                    const char* host,
                    unsigned short port,
                    const char* password);
AP_API int ap_push_pcm(ap_handle* handle,
                       const short* interleaved,
                       unsigned int frames);
AP_API int ap_set_volume(ap_handle* handle, double percent);
AP_API int ap_stop(ap_handle* handle);
AP_API void ap_destroy(ap_handle* handle);
AP_API int ap_get_state(ap_handle* handle);
AP_API unsigned int ap_get_buffer_frames(ap_handle* handle);
AP_API unsigned long long ap_get_dropped_frames(ap_handle* handle);
AP_API const char* ap_last_error(ap_handle* handle);

#ifdef __cplusplus
}
#endif
