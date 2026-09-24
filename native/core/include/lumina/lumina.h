// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  lumina.h : API C plana del núcleo híbrido (vía A: P/Invoke desde C#).
//  Reglas de ABI (contractuales — ver docs/architecture-hybrid.md):
//    * Todo el texto es UTF-8 (sin BOM), len NO cuenta el NUL final.
//    * Sin excepciones cruzando la frontera: códigos de estado.
//    * La DLL no devuelve memoria propia: patrón de búfer
//      fn(..., char* out, int32_t cap, int32_t* needed) — cap=0 solo mide.
//    * Eventos en un hilo dedicado del motor, serializados; el callback
//      NO debe llamar al motor (encolar en el hilo gestionado).
//    * Todas las funciones son seguras ante punteros nulos (devuelven
//      LUMINA_ERR_ARG) y ante handle inválido.
// ============================================================================
#ifndef LUMINA_H
#define LUMINA_H

#include <stdint.h>

#if defined(_WIN32)
  #if defined(LUMINA_CORE_STATIC)
    /* Consumidores internos que enlazan el núcleo estáticamente (tests/PoC) */
    #define LUMINA_EXPORT
  #elif defined(LUMINA_CORE_BUILD)
    #define LUMINA_EXPORT __declspec(dllexport)
  #else
    #define LUMINA_EXPORT __declspec(dllimport)
  #endif
#else
  #define LUMINA_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct LuminaHandle_* LuminaHandle;
typedef int32_t LuminaStatus;

/* Códigos de estado */
enum {
    LUMINA_OK             =  0,
    LUMINA_ERR_ARG        = -1,   /* argumento inválido/nulo            */
    LUMINA_ERR_STATE      = -2,   /* handle o estado no válido          */
    LUMINA_ERR_PARSE      = -3,   /* JSON/.BIB malformado               */
    LUMINA_ERR_IO         = -4,   /* E/S (archivo/BD)                   */
    LUMINA_ERR_LIMIT      = -5,   /* fuera de rango (índice, tamaño)    */
    LUMINA_ERR_UNSUPPORTED= -6    /* operación no soportada (headless)  */
};

/* Eventos (payload UTF-8; el código gestionado decide su interpretación) */
enum {
    LUMINA_EV_LOG           = 1,  /* {"level":"info","msg":"..."}          */
    LUMINA_EV_STATE         = 2,  /* estado JSON completo (igual que state) */
    LUMINA_EV_SLIDE_CHANGED = 3,  /* {"index":N,"item":M,"total":T}        */
    LUMINA_EV_ITEM_CHANGED  = 4,  /* {"item":M,"title":"..."}              */
    LUMINA_EV_ERROR         = 5,  /* {"where":"...","error":"..."}         */
    LUMINA_EV_PONG          = 6,  /* eco de lumina_ping (texto)            */
    LUMINA_EV_PREVIEW       = 7   /* PNG de vista previa (bytes)           */
};

typedef void (*LuminaEventFn)(void* user, int32_t code,
                              const char* payload, int32_t payloadLen);

typedef struct LuminaConfig {
    int32_t       structSize;   /* sizeof(LuminaConfig) — evolución ABI   */
    int32_t       headless;     /* 1 = sin ventanas (tests/API/servicios) */
    LuminaEventFn onEvent;      /* opcional; hilo dedicado del motor      */
    void*         user;         /* contexto del cliente                   */
} LuminaConfig;

/* ---------------------------------------------------------------- ciclo */
LUMINA_EXPORT LuminaHandle lumina_create(const LuminaConfig* cfg);
LUMINA_EXPORT void         lumina_destroy(LuminaHandle h);
LUMINA_EXPORT int32_t      lumina_version(char* out, int32_t cap, int32_t* needed);

/* ------------------------------------------------- escenario / en vivo */
LUMINA_EXPORT int32_t lumina_load_scenario(LuminaHandle h, const char* json, int32_t len);
LUMINA_EXPORT int32_t lumina_show_slide (LuminaHandle h, int32_t index);
LUMINA_EXPORT int32_t lumina_next(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_prev(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_black(LuminaHandle h, int32_t on);   /* 1=negro */
LUMINA_EXPORT int32_t lumina_clear(LuminaHandle h);               /* vacía salida */
LUMINA_EXPORT int32_t lumina_set_theme(LuminaHandle h, const char* json, int32_t len);
LUMINA_EXPORT int32_t lumina_state_json(LuminaHandle h, char* out, int32_t cap, int32_t* needed);
LUMINA_EXPORT int32_t lumina_ping(LuminaHandle h, const char* msg, int32_t len);

/* ------------------------------------------------ proyección (no headless) */
LUMINA_EXPORT int32_t lumina_projector_show(LuminaHandle h, int32_t screenIndex, int32_t fullscreen);
LUMINA_EXPORT int32_t lumina_projector_hide(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_render_preview_png(LuminaHandle h, int32_t slideIndex,
                                                char* out, int32_t cap, int32_t* needed);
/* v6.0.0 «HORIZONTE» — transición entre slides de la proyección (ADITIVA).
   mode: 0=corte (inmediato), 1=fundido (crossfade AlphaBlend sobre el doble
   búfer). durationMs: 0..5000 (fuera de rango → LUMINA_ERR_LIMIT). En headless
   la configuración se guarda y se refleja en el estado ("transition"). */
LUMINA_EXPORT int32_t lumina_set_transition(LuminaHandle h, int32_t mode, int32_t durationMs);

/* ---------------------------------------------------- canciones / biblia */
/* Valida + normaliza una canción JSON y devuelve {"ok":1,"slides":[...],"song":{...}} */
LUMINA_EXPORT int32_t lumina_song_parse(const char* json, int32_t len,
                                        char* out, int32_t cap, int32_t* needed);
/* Valida/analiza un .BIB en memoria; options: {"maxBytes":N,"encoding":"auto"}
   devuelve {"ok":1,"verses":N,"books":N,"version":"...","sample":["..."]} */
LUMINA_EXPORT int32_t lumina_bib_parse(const char* data, int32_t len, const char* optionsJson,
                                       char* out, int32_t cap, int32_t* needed);
/* Resuelve una referencia tipada: -> {"book":43,"chapter":3,"verse":16,"name":"Juan"} */
LUMINA_EXPORT int32_t lumina_bible_ref_resolve(const char* refUtf8,
                                               char* out, int32_t cap, int32_t* needed);
/* Transpone una línea de acordes: {"line":"...","semitones":N,"latin":0|1} */
LUMINA_EXPORT int32_t lumina_chords_transpose(const char* line, int32_t semitones, int32_t latin,
                                              char* out, int32_t cap, int32_t* needed);

/* -------------------------------------------------- v7.0.0 «ULTRA» (ADITIVA)
   F0.01-F0.03: detección de entorno y decisión de arranque (JSON). probeJson
   vacío = sondas reales del sistema; con hechos inyectados = clasificación
   pura (arnés Linux). Devuelve SO, arquitectura, .NET, perfil y notas. */
LUMINA_EXPORT int32_t lumina_env_detect(const char* probeJson,
                                        char* out, int32_t cap, int32_t* needed);

/* F0.09: log nativo estructurado (§10.1). optsJson:
   {"dir":"...","retentionDays":14,"level":"INFO"}; entryJson:
   {"severity":"INFO","module":"render","message":"...","scenarioId":"",
    "elementId":"","lineIndex":-1,"filePath":"","exception":"","callStack":""} */
LUMINA_EXPORT int32_t lumina_log_open(LuminaHandle h, const char* optsJson);
LUMINA_EXPORT int32_t lumina_log_write(LuminaHandle h, const char* entryJson);
LUMINA_EXPORT int32_t lumina_log_stats(LuminaHandle h, char* out, int32_t cap, int32_t* needed);

/* F0.05: IPC ipc.v1 (pipes con nombre, mensajes binarios longitud-prefijada,
   versión ipc.v1, cola no bloqueante con límite de latencia explícito).
   optsJson: {"pipe":"lumina-ipc-v1","maxPayload":N,"maxQueued":N,"maxAgeMs":N} */
LUMINA_EXPORT int32_t lumina_ipc_start(LuminaHandle h, const char* optsJson);
LUMINA_EXPORT int32_t lumina_ipc_stop(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_ipc_stats(LuminaHandle h, char* out, int32_t cap, int32_t* needed);

/* Códec ipc.v1 puro (pruebas de fragmentación/payload/versión + clientes):
   encode produce el frame completo; decoder mantiene estado entre Feed's. */
LUMINA_EXPORT int32_t lumina_ipc_frame_encode(int32_t type, const char* payloadUtf8,
                                              int32_t len, uint8_t* out, int32_t cap,
                                              int32_t* needed);
LUMINA_EXPORT void*    lumina_ipc_decoder_new(void);
LUMINA_EXPORT void     lumina_ipc_decoder_free(void* dec);
/* Feed devuelve: 0=falta, 1=frame (outType/out/needed), <0=código de error
   de protocolo (-1 magic, -2 versión, -3 oversized). consumed = bytes usados. */
LUMINA_EXPORT int32_t  lumina_ipc_decoder_feed(void* dec, const uint8_t* bytes, int32_t len,
                                               int32_t* outType, char* out, int32_t cap,
                                               int32_t* needed, int32_t* consumed);

/* F1.03: sincronización por línea (syncMark). LineNext/LinePrev navegan las
   líneas de la slide activa; LineSet fija el índice (-1 = slide entera). */
LUMINA_EXPORT int32_t lumina_line_next(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_line_prev(LuminaHandle h);
LUMINA_EXPORT int32_t lumina_line_set(LuminaHandle h, int32_t lineIndex);

/* F3.04: Lower Third nativo. json: {"text":"...","position":"bottom|top",
   "durationMs":N,"show":bool} (show=false lo oculta con fundido). */
LUMINA_EXPORT int32_t lumina_lower_third(LuminaHandle h, const char* json);

/* ------------------------------------------------------- almacenamiento */
LUMINA_EXPORT int32_t lumina_db_open(LuminaHandle h, const char* pathUtf8);
LUMINA_EXPORT int32_t lumina_db_close(LuminaHandle h);
/* sqlJson = {"sql":"...","params":[...]} ->
   {"rows":[[...]],"changes":N,"lastId":N}; SELECT limita a 10000 filas. */
LUMINA_EXPORT int32_t lumina_db_exec(LuminaHandle h, const char* sqlJson,
                                     char* out, int32_t cap, int32_t* needed);

#ifdef __cplusplus
}
#endif
#endif /* LUMINA_H */
