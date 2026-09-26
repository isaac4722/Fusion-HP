// ============================================================================
//  Fusion-HP · main.cpp — punto de entrada del núcleo nativo (Win32)
//  FusionHP.exe: bootstrap + motor de proyección + servidor ipc.v1.
//  Compatibilidad: Windows 7 SP1 x86 → Windows 11 x64 [SPEC §1].
// ============================================================================
#include "App.h"
#include "Logger.h"

#include <wil/resource.h>
#include <shellapi.h>

int APIENTRY wWinMain(HINSTANCE inst, HINSTANCE, LPWSTR cmdLine, int showCmd) {
    // Evitar múltiples instancias del núcleo (un solo proyector por equipo)
    // WIL (v4.1.0): RAII — el mutex se cierra solo en toda salida temprana.
    wil::unique_handle mutex(CreateMutexW(nullptr, TRUE, L"Global\\FusionHP.Core.Instance"));
    if (mutex && GetLastError() == ERROR_ALREADY_EXISTS) {
        MessageBoxW(nullptr,
            L"Fusion HP ya está en ejecución.\n\nUsa la ventana existente del estudio.",
            L"Fusion HP", MB_ICONINFORMATION);
        return 0;
    }

    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED | COINIT_DISABLE_OLE1DDE);
    if (FAILED(hr)) {
        MessageBoxW(nullptr, L"No se pudo inicializar COM.", L"Fusion HP", MB_ICONERROR);
        return 1;
    }

    int rc = fusion::App::Get().Run(inst, showCmd);

    CoUninitialize();
    if (mutex) ReleaseMutex(mutex);
    return rc;
}
