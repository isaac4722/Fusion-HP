// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - native/poc-clrhost/main.cpp
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ----------------------------------------------------------------------------
//  PoC de CLR Hosting (vía C de la matriz — docs/architecture-hybrid.md §3):
//  un proceso C++ puro (sin C++/CLI) arranca el CLR 4 y ejecuta código C#
//  de la facade COM-visible «Lumina.PocFacade.dll» vía IDispatch.
//
//  Flujo (todo con RAII simple y Release() garantizado en cada paso):
//    LoadLibrary("mscoree.dll") + GetProcAddress("CLRCreateInstance")
//      -> CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost)
//      -> ICLRMetaHost::GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo)
//      -> ICLRRuntimeInfo::GetInterface(CLSID_CorRuntimeHost, IID_ICorRuntimeHost)
//      -> ICorRuntimeHost::Start()
//      -> ICorRuntimeHost::CreateDomain(L"LuminaPoc", &domain)
//      -> AppDomain (IDispatch)::CreateInstanceFrom(rutaFacade, tipoFacade)
//      -> ObjectHandle (IDispatch)::Unwrap() -> IDispatch* de la facade
//      -> GetIDsOfNames + Invoke:
//           Add(2,3)     == 5
//           Echo(L"híbrido") == L"híbrido"
//           Version()    empieza por "LUMINA-FACADE-OK"
//
//  Salida por stdout: "CLRHOST PASS" y exit 0; cualquier fallo ->
//  "CLRHOST FAIL <detalle>" y exit 1.
//
//  Notas de diseño:
//   * LoadLibrary+GetProcAddress para CLRCreateInstance (más robusto en Win7
//     que enlazar mscoree.lib: no depende del import table ni del SDK usado).
//   * Interfaces CLR (ICLRMetaHost/ICLRRuntimeInfo/ICorRuntimeHost) tomadas de
//     los headers oficiales (metahost.h/mscoree.h) para vtable exacta; los dos
//     CLSID se definen localmente para no depender de constantes del SDK.
//   * CoInitializeEx es opcional para ICorRuntimeHost; se llama por higiene
//     COM y se tolera RPC_E_CHANGED_MODE.
//   * Compilación: x86 y x64, /MT (ver native/poc-clrhost/CMakeLists.txt).
//   * Uso: lumina_poc_clrhost.exe [ruta\Lumina.PocFacade.dll] [TipoFacade]
//     (por defecto: Lumina.PocFacade.dll en CWD y tipo
//      "lumina.poc.LuminaFacade" — clase COM-visible de PocFacade.cs).
// ============================================================================
#include <windows.h>
#include <oaidl.h>     // IDispatch, DISPPARAMS, EXCEPINFO
#include <mscoree.h>   // ICorRuntimeHost (vtable oficial)
#include <metahost.h>  // ICLRMetaHost, ICLRRuntimeInfo
#include <stdio.h>
#include <stdarg.h>
#include <wchar.h>

// ---------------------------------------------------------------------------
// GUIDs locales (valores canónicos documentados; evitan depender de que el
// SDK en uso exporte las constantes CLSID_*/IID_* en el punto de enlace).
// Los IID de ICLRMetaHost/ICLRRuntimeInfo/IDispatch se toman con __uuidof()
// (metahost.h y oaidl.h son MIDL-generados: uuid embebido garantizado);
// mscoree.h no siempre lo está, así que IID_ICorRuntimeHost va local.
// ---------------------------------------------------------------------------
static const GUID kCLSID_CLRMetaHost =
    { 0x92CAE9E0, 0xFC60, 0x4615, { 0x9A, 0x08, 0x1F, 0x49, 0x82, 0xF1, 0x58, 0x55 } };
static const GUID kCLSID_CorRuntimeHost =
    { 0xCB2F6722, 0xAB3A, 0x11D2, { 0x9C, 0x40, 0x00, 0xC0, 0x4F, 0xA3, 0x0A, 0x3E } };
static const GUID kIID_ICorRuntimeHost =
    { 0xCB2F6723, 0xAB3A, 0x11D2, { 0x9C, 0x40, 0x00, 0xC0, 0x4F, 0xA3, 0x0A, 0x3E } };

// ---------------------------------------------------------------------------
// RAII mínimo (sin dependencias: ni ATL, ni com_ptr del SDK)
// ---------------------------------------------------------------------------

// Puntero COM con liberación automática; operator& libera antes de re-usar la
// dirección (patrón clásico para parámetros de salida "out").
template <typename T>
struct ComPtr
{
    T* p;
    ComPtr() : p(nullptr) {}
    ~ComPtr() { Release(); }
    void Release()
    {
        if (p) { p->Release(); p = nullptr; }
    }
    T** operator&()             { Release(); return &p; }
    T*  operator->() const      { return p; }
    T*  Get() const             { return p; }
    bool Ok() const             { return p != nullptr; }
private:
    ComPtr(const ComPtr&);              // no copiable
    ComPtr& operator=(const ComPtr&);   // no asignable
};

// VARIANT inicializado y liberado automáticamente (VariantClear).
struct VariantGuard
{
    VARIANT v;
    VariantGuard() { VariantInit(&v); }
    ~VariantGuard() { VariantClear(&v); }
private:
    VariantGuard(const VariantGuard&);              // no copiable
    VariantGuard& operator=(const VariantGuard&);   // no asignable
};

// ---------------------------------------------------------------------------
// Utilidades de salida y de IDispatch
// ---------------------------------------------------------------------------

static int Fail(const wchar_t* detalle)
{
    wprintf(L"CLRHOST FAIL: %ls\n", detalle);
    fflush(stdout);
    return 1;
}

static int FailHr(const wchar_t* paso, HRESULT hr)
{
    wprintf(L"CLRHOST FAIL: %ls (HRESULT=0x%08lX)\n", paso, (unsigned long)hr);
    fflush(stdout);
    return 1;
}

// Formatea un mensaje de error en un búfer fijo con terminación NUL
// garantizada (_vsnwprintf_s trunca seguro y añade el NUL final).
static void SetErr(wchar_t* buf, size_t cch, const wchar_t* fmt, ...)
{
    va_list ap;
    va_start(ap, fmt);
    _vsnwprintf_s(buf, cch, cch - 1, fmt, ap);
    va_end(ap);
    buf[cch - 1] = L'\0';
}

// GetIDsOfNames de un solo nombre.
static HRESULT GetDispIdOf(IDispatch* disp, const wchar_t* name, DISPID* outId)
{
    OLECHAR* names[1] = { const_cast<OLECHAR*>(name) };
    return disp->GetIDsOfNames(IID_NULL, names, 1, LOCALE_USER_DEFAULT, outId);
}

// Invoke DISPATCH_METHOD con argumentos YA EN ORDEN INVERSO
// (rgvarg[0] = último parámetro), convención de DISPPARAMS.
static HRESULT InvokeMethod(IDispatch* disp, DISPID id,
                            VARIANT* argsReversed, UINT argCount,
                            VARIANT* result)
{
    DISPPARAMS dp;
    ZeroMemory(&dp, sizeof(dp));
    dp.cArgs  = argCount;
    dp.rgvarg = argsReversed;

    EXCEPINFO excep;
    ZeroMemory(&excep, sizeof(excep));
    UINT argErr = 0;
    HRESULT hr = disp->Invoke(id, IID_NULL, LOCALE_USER_DEFAULT,
                              DISPATCH_METHOD, &dp, result, &excep, &argErr);
    if (FAILED(hr) && excep.bstrDescription != nullptr)
    {
        // Diagnóstico del lado gestionado (p. ej. excepción .NET real).
        wprintf(L"  EXCEPINFO: %ls\n", (LPCWSTR)excep.bstrDescription);
        SysFreeString(excep.bstrDescription);
    }
    if (FAILED(hr) && excep.bstrSource != nullptr)
        SysFreeString(excep.bstrSource);
    return hr;
}

// Extrae un IDispatch* de un VARIANT de resultado (lo devuelve AddRef'd).
static bool DispFromVariant(const VARIANT& v, IDispatch** outDisp)
{
    *outDisp = nullptr;
    if (v.vt == VT_DISPATCH && v.pdispVal != nullptr)
    {
        *outDisp = v.pdispVal;
        (*outDisp)->AddRef();
        return true;
    }
    if (v.vt == VT_UNKNOWN && v.punkVal != nullptr)
    {
        // Lo normal: el CCW expone IDispatch (facade ComVisible).
        return SUCCEEDED(v.punkVal->QueryInterface(__uuidof(IDispatch),
                                                   (void**)outDisp)) &&
               *outDisp != nullptr;
    }
    return false;
}

// Valor numérico de un VARIANT resultado (para comparar Add(2,3)==5).
static bool NumericFromVariant(const VARIANT& v, long* outVal)
{
    switch (v.vt)
    {
        case VT_I2: *outVal = v.iVal;     return true;
        case VT_I4: *outVal = v.lVal;     return true;
        case VT_UI2: *outVal = v.uiVal;   return true;
        case VT_UI4: *outVal = (long)v.ulVal; return true;
        case VT_INT: *outVal = v.intVal;  return true;
        case VT_UINT: *outVal = (long)v.uintVal; return true;
        case VT_R8:  *outVal = (long)v.dblVal; return true;
        default: return false;
    }
}

// Crea la facade en un AppDomain dado (CreateInstanceFrom + Unwrap + QI).
static HRESULT CreateFacadeIn(IDispatch* domainDisp,
                              const wchar_t* facadePath,
                              const wchar_t* facadeType,
                              ComPtr<IDispatch>& outFacade,
                              wchar_t* errBuf, size_t cchErr)
{
    DISPID idCreate = DISPID_UNKNOWN;
    HRESULT hr = GetDispIdOf(domainDisp, L"CreateInstanceFrom", &idCreate);
    if (FAILED(hr))
    {
        SetErr(errBuf, cchErr, L"AppDomain no expone CreateInstanceFrom (0x%08lX)",
               (unsigned long)hr);
        return hr;
    }

    // rgvarg[0] = ÚLTIMO parámetro (typeName); rgvarg[1] = assemblyFile (path).
    VariantGuard args[2];
    args[1].v.vt = VT_BSTR;
    args[1].v.bstrVal = SysAllocString(facadePath);
    args[0].v.vt = VT_BSTR;
    args[0].v.bstrVal = SysAllocString(facadeType);
    if (args[1].v.bstrVal == nullptr || args[0].v.bstrVal == nullptr)
    {
        SetErr(errBuf, cchErr, L"Sin memoria para BSTR (CreateInstanceFrom)");
        return E_OUTOFMEMORY;
    }

    VariantGuard vHandle;   // ObjectHandle devuelto
    hr = InvokeMethod(domainDisp, idCreate, &args[0].v, 2, &vHandle.v);
    if (FAILED(hr))
    {
        SetErr(errBuf, cchErr, L"CreateInstanceFrom falló (0x%08lX)",
               (unsigned long)hr);
        return hr;
    }

    ComPtr<IDispatch> dispHandle;
    if (!DispFromVariant(vHandle.v, &dispHandle))
    {
        SetErr(errBuf, cchErr, L"CreateInstanceFrom no devolvió ObjectHandle utilizable (vt=%d)", vHandle.v.vt);
        return E_UNEXPECTED;
    }

    DISPID idUnwrap = DISPID_UNKNOWN;
    hr = GetDispIdOf(dispHandle.Get(), L"Unwrap", &idUnwrap);
    if (FAILED(hr))
    {
        SetErr(errBuf, cchErr, L"ObjectHandle no expone Unwrap (0x%08lX)",
               (unsigned long)hr);
        return hr;
    }

    VariantGuard vFacade;
    hr = InvokeMethod(dispHandle.Get(), idUnwrap, nullptr, 0, &vFacade.v);
    if (FAILED(hr))
    {
        SetErr(errBuf, cchErr, L"Unwrap falló (0x%08lX) — ¿la facade es MarshalByRef/Serializable para cruzar el dominio?", (unsigned long)hr);
        return hr;
    }

    if (!DispFromVariant(vFacade.v, &outFacade))
    {
        SetErr(errBuf, cchErr, L"Unwrap no devolvió objeto con IDispatch (vt=%d) — ¿facade ComVisible?", vFacade.v.vt);
        return E_UNEXPECTED;
    }
    return S_OK;
}

// ---------------------------------------------------------------------------
// main
// ---------------------------------------------------------------------------
int wmain(int argc, wchar_t** argv)
{
    // ---- Parámetros -------------------------------------------------------
    // argv[1]: ruta de Lumina.PocFacade.dll (opcional; por defecto en CWD).
    // argv[2]: nombre completo del tipo facade (opcional; contrato por
    //          defecto: namespace Lumina.PocFacade, clase PocFacade).
    const wchar_t* dllArg  = (argc > 1) ? argv[1] : L"Lumina.PocFacade.dll";
    const wchar_t* facadeType = (argc > 2) ? argv[2] : L"lumina.poc.LuminaFacade";

    wchar_t facadePath[1024];
    DWORD nPath = GetFullPathNameW(dllArg,
                                   (DWORD)(sizeof(facadePath) / sizeof(wchar_t)),
                                   facadePath, nullptr);
    if (nPath == 0 || nPath >= (DWORD)(sizeof(facadePath) / sizeof(wchar_t)) ||
        GetFileAttributesW(facadePath) == INVALID_FILE_ATTRIBUTES)
    {
        return Fail(L"no se encontró la DLL facade (pase la ruta como argv[1])");
    }

    // ---- COM por higiene (opcional: ICorRuntimeHost no lo exige) ----------
    HRESULT hrInit = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    // RPC_E_CHANGED_MODE: ya hay un apartamento (MTA) — también sirve.
    const bool comInitialized = SUCCEEDED(hrInit) || hrInit == RPC_E_CHANGED_MODE;

    // ---- 1) mscoree.dll: ruta directa del shim .NET 4 (determinista) ------
    //  El System32\mscoree.dll de algunos sistemas/runners puede responder
    //  E_NOINTERFACE; el shim REAL vive junto al runtime instalado:
    //    x64 → %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\mscoree.dll
    //    x86 → %WINDIR%\Microsoft.NET\Framework\v4.0.30319\mscoree.dll
    //  Se intenta la ruta directa, luego el nombre simple (Win7 SP1 OK).
    HMODULE hMscoree = nullptr;
    {
        wchar_t path[MAX_PATH];
        // El bitness del PROCESO decide la carpeta del runtime (Framework64/Framework);
        // se prueban AMBAS rutas (por si el runtime instalado es de otro bitness).
        const wchar_t* dirs[2] = { L"Microsoft.NET\\Framework64", L"Microsoft.NET\\Framework" };
        UINT k = GetEnvironmentVariableW(L"WINDIR", path, MAX_PATH);
        for (int i = 0; i < 2 && hMscoree == nullptr && k > 0 && k < MAX_PATH - 64; ++i) {
            wchar_t full[MAX_PATH];
            _snwprintf_s(full, MAX_PATH, MAX_PATH - 1,
                         L"%ls\\%ls\\v4.0.30319\\mscoree.dll", path, dirs[i]);
            full[MAX_PATH - 1] = 0;
            const DWORD attr = GetFileAttributesW(full);
            wprintf(L"  ruta %ls → %ls\n", dirs[i],
                    attr != INVALID_FILE_ATTRIBUTES ? L"presente" : L"ausente");
            if (attr != INVALID_FILE_ATTRIBUTES)
                hMscoree = LoadLibraryExW(full, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
            if (hMscoree == nullptr)
                wprintf(L"  LoadLibraryEx(%ls) falló (GetLastError=0x%08lX)\n",
                        full, (unsigned long)GetLastError());
        }
        // Ambas rutas fuerzan la carga; si ninguna, va el nombre simple abajo.
        if (hMscoree == nullptr)
            hMscoree = LoadLibraryW(L"mscoree.dll");
    }
    if (hMscoree == nullptr)
        return Fail(L"mscoree.dll no se pudo cargar (.NET Framework ausente o corrupto)");

    // Diagnóstico: módulo realmente cargado
    {
        wchar_t modPath[MAX_PATH] = L"?";
        GetModuleFileNameW(hMscoree, modPath, MAX_PATH);
        wprintf(L"  mscoree cargado: %ls\n", modPath);
        fflush(stdout);
    }

    typedef HRESULT (STDAPICALLTYPE* PFN_CLRCreateInstance)(REFCLSID, REFIID, void**);
    typedef HRESULT (STDAPICALLTYPE* PFN_CorBind)(LPCWSTR, LPCWSTR, DWORD,
                                                  REFCLSID, REFIID, void**);
    PFN_CLRCreateInstance pClrCreateInstance =
        (PFN_CLRCreateInstance)GetProcAddress(hMscoree, "CLRCreateInstance");
    PFN_CorBind pCorBind =
        (PFN_CorBind)GetProcAddress(hMscoree, "CorBindToRuntimeEx");

    // ---- 2) Activación multinivel hasta ICorRuntimeHost --------------------
    //  a) CLRCreateInstance (metahost) — vía moderna .NET 4
    //  b) CorBindToRuntimeEx — vía clásica (2.0/4.x), misma ICorRuntimeHost
    ComPtr<ICorRuntimeHost> corHost;
    HRESULT hr = E_FAIL;
    if (pClrCreateInstance != nullptr)
    {
        ComPtr<ICLRMetaHost> metaHost;
        hr = pClrCreateInstance(kCLSID_CLRMetaHost, __uuidof(ICLRMetaHost),
                                (void**)&metaHost);
        if (SUCCEEDED(hr))
        {
            ComPtr<ICLRRuntimeInfo> runtimeInfo;
            hr = metaHost->GetRuntime(L"v4.0.30319", __uuidof(ICLRRuntimeInfo),
                                      (void**)&runtimeInfo);
            if (SUCCEEDED(hr))
                hr = runtimeInfo->GetInterface(kCLSID_CorRuntimeHost,
                                               kIID_ICorRuntimeHost,
                                               (void**)&corHost);
        }
        if (SUCCEEDED(hr))
            wprintf(L"  activación: CLRCreateInstance (metahost)\n");
    }
    if ((FAILED(hr) || !corHost.Ok()) && pCorBind != nullptr)
    {
        // CorBindToRuntimeEx(vVersion, flavor, flags, rclsid, riid, ppv)
        hr = pCorBind(L"v4.0.30319", L"wks", 0,
                      kCLSID_CorRuntimeHost, kIID_ICorRuntimeHost,
                      (void**)&corHost);
        if (SUCCEEDED(hr))
            wprintf(L"  activación: CorBindToRuntimeEx (legacy)\n");
    }
    if (FAILED(hr) || !corHost.Ok())
    {
        wprintf(L"  (aviso) CLRCreateInstance HR=0x%08lX\n", (unsigned long)hr);
        return FailHr(L"activación del CLR (metahost + CorBindToRuntimeEx)", hr);
    }

    hr = corHost->Start();
    if (FAILED(hr))
        return FailHr(L"ICorRuntimeHost::Start", hr);

    // ---- 3) Dominio "LuminaPoc" (fallback: dominio por defecto) -----------
    ComPtr<IUnknown> unkDomain;
    hr = corHost->CreateDomain(L"LuminaPoc", nullptr, &unkDomain);
    if (FAILED(hr) || !unkDomain.Ok())
        return FailHr(L"ICorRuntimeHost::CreateDomain(LuminaPoc)", hr);

    ComPtr<IDispatch> dispDomain;
    hr = unkDomain->QueryInterface(__uuidof(IDispatch), (void**)&dispDomain);
    if (FAILED(hr))
        return FailHr(L"AppDomain::QueryInterface(IID_IDispatch)", hr);

    wchar_t errBuf[512];
    ComPtr<IDispatch> facade;
    hr = CreateFacadeIn(dispDomain.Get(), facadePath, facadeType, facade,
                        errBuf, 512);
    if (FAILED(hr))
    {
        // Plan B: algunos objetos no cruzan dominios sin MarshalByRef/Serializable.
        // El hosting es igual de válido sobre el dominio por defecto.
        wprintf(L"  (aviso) dominio LuminaPoc: %ls — reintentando con el dominio por defecto\n", errBuf);
        fflush(stdout);

        ComPtr<IUnknown> unkDefault;
        hr = corHost->GetDefaultDomain(&unkDefault);   // ICorRuntimeHost::GetDefaultDomain
        if (FAILED(hr) || !unkDefault.Ok())
            return FailHr(L"ICorRuntimeHost::GetDefaultDomain", hr);

        ComPtr<IDispatch> dispDefault;
        hr = unkDefault->QueryInterface(__uuidof(IDispatch), (void**)&dispDefault);
        if (FAILED(hr))
            return FailHr(L"AppDomain por defecto::QueryInterface(IID_IDispatch)", hr);

        hr = CreateFacadeIn(dispDefault.Get(), facadePath, facadeType, facade,
                            errBuf, 512);
        if (FAILED(hr))
            return Fail(errBuf);
    }

    // ---- 4) Pruebas funcionales por IDispatch ------------------------------

    // Add(2,3) == 5
    DISPID idAdd = DISPID_UNKNOWN;
    if (FAILED(GetDispIdOf(facade.Get(), L"Add", &idAdd)))
        return Fail(L"la facade no expone Add");
    VariantGuard argsAdd[2];
    argsAdd[0].v.vt = VT_I4; argsAdd[0].v.lVal = 3;   // rgvarg[0] = último (b)
    argsAdd[1].v.vt = VT_I4; argsAdd[1].v.lVal = 2;   // rgvarg[1] = primero (a)
    VariantGuard vAdd;
    hr = InvokeMethod(facade.Get(), idAdd, &argsAdd[0].v, 2, &vAdd.v);
    if (FAILED(hr))
        return FailHr(L"facade.Add(2,3)", hr);
    long addVal = 0;
    if (!NumericFromVariant(vAdd.v, &addVal) || addVal != 5)
        return Fail(L"Add(2,3) != 5 (facade no verificada)");

    // Echo(L"híbrido") == L"híbrido"
    DISPID idEcho = DISPID_UNKNOWN;
    if (FAILED(GetDispIdOf(facade.Get(), L"Echo", &idEcho)))
        return Fail(L"la facade no expone Echo");
    VariantGuard argsEcho[1];
    argsEcho[0].v.vt = VT_BSTR;
    argsEcho[0].v.bstrVal = SysAllocString(L"h\u00EDbrido"); // 'í' via escape: inmune a la codepage de la fuente
    if (argsEcho[0].v.bstrVal == nullptr)
        return Fail(L"sin memoria para BSTR (Echo)");
    VariantGuard vEcho;
    hr = InvokeMethod(facade.Get(), idEcho, &argsEcho[0].v, 1, &vEcho.v);
    if (FAILED(hr))
        return FailHr(L"facade.Echo(L\"híbrido\")", hr);
    if (vEcho.v.vt != VT_BSTR || vEcho.v.bstrVal == nullptr ||
        wcscmp(vEcho.v.bstrVal, L"h\u00EDbrido") != 0)
        return Fail(L"Echo(L\"híbrido\") no devolvió el eco esperado (¿marshaling UTF-16 roto?)");

    // Version() empieza por "LUMINA-FACADE-OK"
    DISPID idVersion = DISPID_UNKNOWN;
    if (FAILED(GetDispIdOf(facade.Get(), L"Version", &idVersion)))
        return Fail(L"la facade no expone Version");
    VariantGuard vVersion;
    hr = InvokeMethod(facade.Get(), idVersion, nullptr, 0, &vVersion.v);
    if (FAILED(hr))
        return FailHr(L"facade.Version()", hr);
    if (vVersion.v.vt != VT_BSTR || vVersion.v.bstrVal == nullptr ||
        wcsncmp(vVersion.v.bstrVal, L"LUMINA-FACADE-OK", 16) != 0)
        return Fail(L"Version() no empieza por \"LUMINA-FACADE-OK\"");

    // ---- 5) Parada limpia ---------------------------------------------------
    corHost->Stop();   // opcional: apaga el CLR antes de salir

    if (comInitialized)
        CoUninitialize();

    wprintf(L"CLRHOST PASS\n");
    fflush(stdout);
    return 0;
}
