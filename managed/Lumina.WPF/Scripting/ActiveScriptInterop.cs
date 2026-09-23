// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ActiveScriptInterop.cs (v6.1.0 «GUION») : declaraciones de interop COM para
//  hospedar el motor JScript del PROPIO Windows (IActiveScript) — el requisito
//  §3.3 del spec («evaluación con IActiveScript/JScript del propio Windows,
//  ES3, cero despliegue») sin agregados, sin NuGet y sin JVM/Electron.
//
//  Fidelidad de vtable: los métodos están declarados EN EL ORDEN EXACTO de
//  ActivScp.h del SDK de Windows (un slot mal ubicado = llamada a un método
//  equivocado y corrupción silenciosa — por eso IActiveScript declara los 13
//  métodos aunque solo usemos 5, e IActiveScriptParse declara AddScriptlet
//  aunque nunca se llame: ocupa su slot de vtable).
//
//  ⚠ LECCIÓN DEL GATE DE CI (primer run del tag v6.1.0-beta.1): el QI por
//  IActiveScriptParse devolvía E_NOINTERFACE porque esta interfaz tiene
//  **IID DISTINTA POR ARQUITECTURA** — su vtable lleva un DWORD_PTR
//  (dwSourceContextCookie, tamaño de puntero): IID 32 bits
//  BB1A2AE2-A4F9-11CF-8F20-00805F2CD064 · IID 64 bits
//  C7EF7658-E1EE-480E-97EA-D52CB4D76D17 (fuente: activscp.idl del SDK/Wine).
//  El motor consulta la de SU bitness y responde E_NOINTERFACE a la otra —
//  por eso aquí se declaran IActiveScriptParse32/IActiveScriptParse64 con la
//  MISMA forma (UIntPtr para el cookie) y el JsEngine se queda con la que
//  acepte el QueryInterface.
//
//  GUID (activscp.idl — verificados contra el run de CI):
//    CLSID_JScript            F414C260-6AC0-11CF-B6D1-00AA00BBBB58 (jscript.dll,
//                             presente de Win7 SP1 a Win11 — cero despliegue)
//    IID_IActiveScript        BB1A2AE1-A4F9-11CF-8F20-00805F2CD064
//    IID_IActiveScriptParse32 BB1A2AE2-A4F9-11CF-8F20-00805F2CD064
//    IID_IActiveScriptParse64 C7EF7658-E1EE-480E-97EA-D52CB4D76D17
//    IID_IActiveScriptSite    DB01A1E3-A42B-11CF-8F20-00805F2CD064
//    IID_IActiveScriptError   EAE1BA61-A4ED-11CF-8F20-00805F2CD064
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace lumina.wpf.scripting
{
    /// <summary>SCRIPTSTATE (ActivScp.h) — ciclo de vida del motor de scripts.</summary>
    internal enum ScriptState
    {
        Uninitialized = 0,
        Started = 1,
        Connected = 2,
        Disconnected = 3,
        Closed = 4,
        Initialized = 5,
    }

    /// <summary>SCRIPTSTATE de un hilo de script (solo declarado por vtable).</summary>
    internal enum ScriptThreadState
    {
        NotStarted = 0,
        Running = 1,
        Suspended = 2,
        Blocked = 3,
    }

    /// <summary>EXCEPINFO (oaidl.h) con los BSTR como IntPtr: se leen con
    /// PtrToStringBSTR y se liberan con FreeBSTR (el motor los asigna para el
    /// llamador — no liberarlos es una fuga por cada error de sintaxis).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExcepInfo
    {
        public ushort wCode;
        public ushort wReserved;
        public IntPtr bstrSource;
        public IntPtr bstrDescription;
        public IntPtr bstrHelpFile;
        public uint dwHelpContext;
        public IntPtr pvReserved;
        public IntPtr pfnDeferredFillIn;
        public int scode;
    }

    /// <summary>SCRIPTITEM_ISVISIBLE|SCRIPTITEM_ISPERSISTENT para AddNamedItem:
    /// el ítem queda visible bajo su nombre («jslib.xxx» desde JScript) y
    /// sobrevive a las transiciones de estado del motor.</summary>
    internal static class ScriptItem
    {
        public const uint IsVisible = 0x2;
        public const uint IsPersistent = 0x40;
    }

    /// <summary>SCRIPTINFO_* de GetItemInfo: solo entregamos IUNKNOWN del host
    /// (la resolución de miembros es tardía por IDispatch — sin TypeInfo).</summary>
    internal static class ScriptInfo
    {
        public const uint IUnknown = 0x1;
        public const uint ITypeInfo = 0x2;
    }

    // ------------------------------------------------------------------ error

    /// <summary>IActiveScriptError — lo llama el motor vía OnScriptError con la
    /// línea/columna y la descripción del error (los tres métodos en orden).</summary>
    [ComImport, Guid("EAE1BA61-A4ED-11cf-8F20-00805F2CD064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActiveScriptError
    {
        void GetExceptionInfo(ref ExcepInfo info);
        void GetSourcePosition(out uint sourceContextCookie, out uint lineNumber, out int charPosition);
        void GetSourceLineText([MarshalAs(UnmanagedType.BStr)] out string sourceLine);
    }

    // ------------------------------------------------------------------- site

    /// <summary>IActiveScriptSite — el «contenedor» que el motor llama de vuelta:
    /// GetItemInfo entrega el objeto «jslib», OnScriptError reporta errores de
    /// sintaxis/ejecución con posición. Implementación: ActiveScriptSite (en
    /// JsEngine.cs). 8 métodos en orden de vtable.</summary>
    [ComImport, Guid("DB01A1E3-A42B-11cf-8F20-00805F2CD064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActiveScriptSite
    {
        void GetLCID(out uint lcid);
        void GetItemInfo([MarshalAs(UnmanagedType.LPWStr)] string name, uint mask,
                         out IntPtr item, out IntPtr typeInfo);
        void GetDocVersionString([MarshalAs(UnmanagedType.BStr)] out string version);
        void OnScriptTerminate(IntPtr varResult, IntPtr excepInfo);
        void OnStateChange(ScriptState state);
        void OnScriptError(IActiveScriptError scriptError);
        void OnEnterScript();
        void OnLeaveScript();
    }

    // ------------------------------------------------------------------ parse

    /// <summary>
    /// IActiveScriptParse — InitNew + ParseScriptText (AddScriptlet declarado
    /// por fidelidad de vtable, jamás se llama). 3 métodos.
    ///
    /// ⚠ POR ARQUITECTURA: dwSourceContextCookie es DWORD_PTR (tamaño de
    /// puntero) → el IID y la vtable cambian entre x86 y x64. Declaramos las
    /// DOS formas (32/64) con UIntPtr (tamaño de puntero en cada proceso) y
    /// el motor responde a la de SU bitness: JsEngine consulta ambas con «as»
    /// (QI) y se queda con la que acepte.
    /// </summary>
    [ComImport, Guid("BB1A2AE2-A4F9-11cf-8F20-00805F2CD064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActiveScriptParse32
    {
        void InitNew();
        void AddScriptlet(
            [MarshalAs(UnmanagedType.LPWStr)] string defaultName,
            [MarshalAs(UnmanagedType.LPWStr)] string code,
            [MarshalAs(UnmanagedType.LPWStr)] string itemName,
            IntPtr context,
            [MarshalAs(UnmanagedType.LPWStr)] string delimiter,
            UIntPtr sourceContextCookie,
            uint startingLineNumber,
            uint flags,
            IntPtr varResult,
            IntPtr excepInfo);
        void ParseScriptText(
            [MarshalAs(UnmanagedType.LPWStr)] string code,
            [MarshalAs(UnmanagedType.LPWStr)] string itemName,
            IntPtr context,
            [MarshalAs(UnmanagedType.LPWStr)] string delimiter,
            UIntPtr sourceContextCookie,
            uint startingLineNumber,
            uint flags,
            IntPtr varResult,
            IntPtr excepInfo);
    }

    /// <summary>IActiveScriptParse para proceso de 64 bits (misma forma,
    /// IID propia del vtable x64 — ver la lección en la cabecera).</summary>
    [ComImport, Guid("C7EF7658-E1EE-480E-97EA-D52CB4D76D17"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActiveScriptParse64
    {
        void InitNew();
        void AddScriptlet(
            [MarshalAs(UnmanagedType.LPWStr)] string defaultName,
            [MarshalAs(UnmanagedType.LPWStr)] string code,
            [MarshalAs(UnmanagedType.LPWStr)] string itemName,
            IntPtr context,
            [MarshalAs(UnmanagedType.LPWStr)] string delimiter,
            UIntPtr sourceContextCookie,
            uint startingLineNumber,
            uint flags,
            IntPtr varResult,
            IntPtr excepInfo);
        void ParseScriptText(
            [MarshalAs(UnmanagedType.LPWStr)] string code,
            [MarshalAs(UnmanagedType.LPWStr)] string itemName,
            IntPtr context,
            [MarshalAs(UnmanagedType.LPWStr)] string delimiter,
            UIntPtr sourceContextCookie,
            uint startingLineNumber,
            uint flags,
            IntPtr varResult,
            IntPtr excepInfo);
    }

    // ----------------------------------------------------------------- engine

    /// <summary>IActiveScript — el motor JScript. 13 métodos en orden de vtable
    /// (GetScriptDispatch declara object con MarshalAs IDispatch: «out object»
    /// A SECAS se marshala como VARIANT* y el marshaler lanza «Specified OLE
    /// variant is invalid» — lección del segundo run del tag).</summary>
    [ComImport, Guid("BB1A2AE1-A4F9-11cf-8F20-00805F2CD064"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActiveScript
    {
        void SetScriptSite(IActiveScriptSite site);
        void GetScriptSite(ref Guid riid, out IntPtr ppvObject);
        void SetScriptState(ScriptState state);
        void GetScriptState(out ScriptState state);
        void Close();
        void AddNamedItem([MarshalAs(UnmanagedType.LPWStr)] string name, uint flags);
        void AddTypeLib(ref Guid typeLib, uint major, uint minor, uint flags);
        void GetScriptDispatch([MarshalAs(UnmanagedType.LPWStr)] string itemName,
                               [MarshalAs(UnmanagedType.IDispatch)] out object dispatch);
        void GetCurrentScriptThreadID(out uint threadId);
        void GetScriptThreadID(uint win32ThreadId, out uint scriptThreadId);
        void GetScriptThreadState(uint scriptThreadId, out ScriptThreadState state);
        void Interrupt();
        void Clone(out IActiveScript engine);
    }

    /// <summary>Activación del CLSID_JScript (jscript.dll). Type.GetTypeFromCLSID
    /// + Activator.CreateInstance + cast (el cast hace el QueryInterface).</summary>
    internal static class ActiveScriptInterop
    {
        /// <summary>CLSID del motor JScript clásico (ES3) de Windows.</summary>
        public static readonly Guid JScriptClsid = new Guid("F414C260-6AC0-11CF-B6D1-00AA00BBBB58");

        /// <summary>Crea el motor JScript (jscript.dll). Lanza con mensaje claro
        /// si el registro COM no lo encuentra (REGDB_E_CLASSNOTREG).</summary>
        public static IActiveScript CreateJScriptEngine()
        {
            Type t = Type.GetTypeFromCLSID(JScriptClsid);
            if (t == null)
            {
                throw new InvalidOperationException(
                    "No se pudo resolver el motor JScript de Windows (CLSID JScript).");
            }
            object obj = Activator.CreateInstance(t);
            try
            {
                return (IActiveScript)obj;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "El motor JScript de Windows no respondió a IActiveScript (¿jscript.dll?): "
                    + ex.Message, ex);
            }
        }
    }
}
