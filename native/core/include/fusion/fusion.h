// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  fusion.h : API C plana del núcleo híbrido (vía A: P/Invoke desde C#).
//  Reglas de ABI (contractuales — ver docs/architecture-hybrid.md):
//    * Todo el texto es UTF-8 (sin BOM), len NO cuenta el NUL final.
//    * Sin excepciones cruzando la frontera: códigos de estado.
//    * La DLL no devuelve memoria propia: patrón de búfer
//      fn(..., char* out, int32_t cap, int32_t* needed) — cap=0 solo mide.
//    * Eventos en un hilo dedicado del motor, serializados; el callback
//      NO debe llamar al motor (encolar en el hilo gestionado).
//    * Todas las funciones son seguras ante punteros nulos (devuelven
//      FUSION_ERR_ARG) y ante handle inválido.
// ============================================================================
#ifndef FUSION_H
#define FUSION_H

#include <stdint.h>

#if defined(_WIN32)
  #if defined(FUSION_CORE_STATIC)
    /* Consumidores internos que enlazan el núcleo estáticamente (tests/PoC) */
    #define FUSION_EXPORT
  #elif defined(FUSION_CORE_BUILD)
    #define FUSION_EXPORT __declspec(dllexport)
  #else
    #define FUSION_EXPORT __declspec(dllimport)
  #endif
#else
  #define FUSION_EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct FusionHandle_* FusionHandle;
typedef int32_t FusionStatus;

/* Códigos de estado */
enum {
    FUSION_OK             =  0,
    FUSION_ERR_ARG        = -1,   /* argumento inválido/nulo            */
    FUSION_ERR_STATE      = -2,   /* handle o estado no válido          */
    FUSION_ERR_PARSE      = -3,   /* JSON/.BIB malformado               */
    FUSION_ERR_IO         = -4,   /* E/S (archivo/BD)                   */
    FUSION_ERR_LIMIT      = -5,   /* fuera de rango (índice, tamaño)    */
    FUSION_ERR_UNSUPPORTED= -6    /* operación no soportada (headless)  */
};

/* Eventos (payload UTF-8; el código gestionado decide su interpretación) */
enum {
    FUSION_EV_LOG           = 1,  /* {"level":"info","msg":"..."}          */
    FUSION_EV_STATE         = 2,  /* estado JSON completo (igual que state) */
    FUSION_EV_SLIDE_CHANGED = 3,  /* {"index":N,"item":M,"total":T}        */
    FUSION_EV_ITEM_CHANGED  = 4,  /* {"item":M,"title":"..."}              */
    FUSION_EV_ERROR         = 5,  /* {"where":"...","error":"..."}         */
    FUSION_EV_PONG          = 6,  /* eco de fusion_ping (texto)            */
    FUSION_EV_PREVIEW       = 7   /* PNG de vista previa (bytes)           */
};

typedef void (*FusionEventFn)(void* user, int32_t code,
                              const char* payload, int32_t payloadLen);

typedef struct FusionConfig {
    int32_t       structSize;   /* sizeof(FusionConfig) — evolución ABI   */
    int32_t       headless;     /* 1 = sin ventanas (tests/API/servicios) */
    FusionEventFn onEvent;      /* opcional; hilo dedicado del motor      */
    void*         user;         /* contexto del cliente                   */
} FusionConfig;

/* ---------------------------------------------------------------- ciclo */
FUSION_EXPORT FusionHandle fusion_create(const FusionConfig* cfg);
FUSION_EXPORT void         fusion_destroy(FusionHandle h);
FUSION_EXPORT int32_t      fusion_version(char* out, int32_t cap, int32_t* needed);

/* ------------------------------------------------- escenario / en vivo */
FUSION_EXPORT int32_t fusion_load_scenario(FusionHandle h, const char* json, int32_t len);
FUSION_EXPORT int32_t fusion_show_slide (FusionHandle h, int32_t index);
FUSION_EXPORT int32_t fusion_next(FusionHandle h);
FUSION_EXPORT int32_t fusion_prev(FusionHandle h);
FUSION_EXPORT int32_t fusion_black(FusionHandle h, int32_t on);   /* 1=negro */
FUSION_EXPORT int32_t fusion_clear(FusionHandle h);               /* vacía salida */
FUSION_EXPORT int32_t fusion_set_theme(FusionHandle h, const char* json, int32_t len);
FUSION_EXPORT int32_t fusion_state_json(FusionHandle h, char* out, int32_t cap, int32_t* needed);
FUSION_EXPORT int32_t fusion_ping(FusionHandle h, const char* msg, int32_t len);

/* ------------------------------------------------ proyección (no headless) */
FUSION_EXPORT int32_t fusion_projector_show(FusionHandle h, int32_t screenIndex, int32_t fullscreen);
FUSION_EXPORT int32_t fusion_projector_hide(FusionHandle h);
FUSION_EXPORT int32_t fusion_render_preview_png(FusionHandle h, int32_t slideIndex,
                                                char* out, int32_t cap, int32_t* needed);

/* ---------------------------------------------------- canciones / biblia */
/* Valida + normaliza una canción JSON y devuelve {"ok":1,"slides":[...],"song":{...}} */
FUSION_EXPORT int32_t fusion_song_parse(const char* json, int32_t len,
                                        char* out, int32_t cap, int32_t* needed);
/* Valida/analiza un .BIB en memoria; options: {"maxBytes":N,"encoding":"auto"}
   devuelve {"ok":1,"verses":N,"books":N,"version":"...","sample":["..."]} */
FUSION_EXPORT int32_t fusion_bib_parse(const char* data, int32_t len, const char* optionsJson,
                                       char* out, int32_t cap, int32_t* needed);
/* Resuelve una referencia tipada: -> {"book":43,"chapter":3,"verse":16,"name":"Juan"} */
FUSION_EXPORT int32_t fusion_bible_ref_resolve(const char* refUtf8,
                                               char* out, int32_t cap, int32_t* needed);
/* Transpone una línea de acordes: {"line":"...","semitones":N,"latin":0|1} */
FUSION_EXPORT int32_t fusion_chords_transpose(const char* line, int32_t semitones, int32_t latin,
                                              char* out, int32_t cap, int32_t* needed);

/* ------------------------------------------------------- almacenamiento */
FUSION_EXPORT int32_t fusion_db_open(FusionHandle h, const char* pathUtf8);
FUSION_EXPORT int32_t fusion_db_close(FusionHandle h);
/* sqlJson = {"sql":"...","params":[...]} ->
   {"rows":[[...]],"changes":N,"lastId":N}; SELECT limita a 10000 filas. */
FUSION_EXPORT int32_t fusion_db_exec(FusionHandle h, const char* sqlJson,
                                     char* out, int32_t cap, int32_t* needed);

#ifdef __cplusplus
}
#endif
#endif /* FUSION_H */
