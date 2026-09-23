// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MidiInput.cs : entrada MIDI por winmm.dll (Windows Multimedia, presente en
//  Win7→Win11 — cero instalaciones; requisito spec §3.3 «recepción de comandos
//  MIDI» para los activadores).
//
//  P/Invoke net35-safe:
//    * El delegado del callback se enraiza (campo + GCHandle) — patrón del
//      puente del motor (LuminaEngine).
//    * El callback corre en el HILO DEL DRIVER (winmm): los eventos se
//      entregan tal cual; el suscriptor los encola a su contexto (igual que
//      los eventos del motor nativo).
//    * midiInReset antes de midiInClose: exige devolver los buffers; con
//      mensajes cortos basta Reset (drena los sysex en curso).
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace lumina.ui
{
    /// <summary>Evento MIDI normalizado (ya decodificado del mensaje corto).</summary>
    public sealed class MidiEventArgs : EventArgs
    {
        public int Channel;    // 0..15
        public string Command; // "note" | "note_off" | "cc" | "program" | "pitch" | "other"
        public int Data1;      // nota / controlador / programa
        public int Data2;      // velocidad / valor
        public int RawStatus;  // byte de estado crudo
    }

    public sealed class MidiInput : IDisposable
    {
        // ---------------------------------------------------------- winmm (ANSI)
        [DllImport("winmm.dll", EntryPoint = "midiInGetNumDevs")]
        private static extern int midiInGetNumDevs();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
        }

        [DllImport("winmm.dll", EntryPoint = "midiInGetDevCapsA", CharSet = CharSet.Ansi)]
        private static extern int midiInGetDevCapsA(uint device, ref MidiInCaps caps, int size);

        private delegate void MidiInProc(IntPtr h, uint msg, IntPtr instance, IntPtr param1, IntPtr param2);

        [DllImport("winmm.dll", EntryPoint = "midiInOpen")]
        private static extern int midiInOpen(out IntPtr handle, uint device,
                                             MidiInProc callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll", EntryPoint = "midiInStart")]
        private static extern int midiInStart(IntPtr handle);

        [DllImport("winmm.dll", EntryPoint = "midiInStop")]
        private static extern int midiInStop(IntPtr handle);

        [DllImport("winmm.dll", EntryPoint = "midiInReset")]
        private static extern int midiInReset(IntPtr handle);

        [DllImport("winmm.dll", EntryPoint = "midiInClose")]
        private static extern int midiInClose(IntPtr handle);

        private const uint CallbackFunction = 0x30000;
        private const uint MmMimData = 0x3C3;   // mensaje corto

        private IntPtr _handle;
        private MidiInProc _callback;         // RAÍZ (el GC nunca la recolecta)
        private GCHandle _callbackGuard;
        private volatile bool _disposed;

        /// <summary>Evento MIDI entrante (HILO DEL DRIVER: no bloquear aquí).</summary>
        public event EventHandler<MidiEventArgs> MessageReceived;

        /// <summary>Última decisión de abrir el dispositivo (para logs/UI).</summary>
        public string LastError = string.Empty;

        public static int DeviceCount()
        {
            try { return midiInGetNumDevs(); }
            catch (Exception) { return 0; }   // winmm ausente (no debería en Windows)
        }

        public static string[] DeviceNames()
        {
            int n = DeviceCount();
            string[] names = new string[n];
            for (int i = 0; i < n; i++)
            {
                try
                {
                    MidiInCaps caps = new MidiInCaps();
                    caps.Name = new string('\0', 32);
                    midiInGetDevCapsA((uint)i, ref caps, Marshal.SizeOf(typeof(MidiInCaps)));
                    names[i] = (caps.Name ?? string.Empty).Trim();
                    if (names[i].Length == 0) names[i] = "Dispositivo MIDI " + i;
                }
                catch (Exception)
                {
                    names[i] = "Dispositivo MIDI " + i;
                }
            }
            return names;
        }

        /// <summary>Abre y arranca el dispositivo (0-based). true = OK.</summary>
        public bool Open(int deviceIndex)
        {
            Close();
            if (deviceIndex < 0 || deviceIndex >= DeviceCount())
            {
                LastError = "Índice de dispositivo MIDI fuera de rango.";
                return false;
            }
            _callback = new MidiInProc(OnMidiMessage);
            _callbackGuard = GCHandle.Alloc(_callback);
            IntPtr h;
            int mmr = midiInOpen(out h, (uint)deviceIndex, _callback, IntPtr.Zero, CallbackFunction);
            if (mmr != 0)
            {
                FreeCallback();
                LastError = "midiInOpen falló (MMR " + mmr + "). ¿otra app usa el dispositivo?";
                return false;
            }
            _handle = h;
            mmr = midiInStart(_handle);
            if (mmr != 0)
            {
                try { midiInClose(_handle); } catch (Exception) { }
                _handle = IntPtr.Zero;
                FreeCallback();
                LastError = "midiInStart falló (MMR " + mmr + ").";
                return false;
            }
            LastError = string.Empty;
            return true;
        }

        public bool IsOpen { get { return _handle != IntPtr.Zero; } }

        // Se ejecuta en el hilo del driver winmm: mínimo trabajo, cero excepciones.
        private void OnMidiMessage(IntPtr h, uint msg, IntPtr instance, IntPtr param1, IntPtr param2)
        {
            if (_disposed || h != _handle) return;
            if (msg != MmMimData) return;          // solo mensajes cortos
            try
            {
                int data = (int)(param1.ToInt64() & 0xFFFFFF);   // status | d1<<8 | d2<<16
                int status = data & 0xFF;
                int d1 = (data >> 8) & 0x7F;
                int d2 = (data >> 16) & 0x7F;
                int hi = status & 0xF0;

                MidiEventArgs ev = new MidiEventArgs();
                ev.RawStatus = status;
                ev.Channel = status & 0x0F;
                ev.Data1 = d1;
                ev.Data2 = d2;
                if (hi == 0x90 && d2 > 0) { ev.Command = CmdNote; }
                else if (hi == 0x80 || (hi == 0x90 && d2 == 0)) { ev.Command = CmdNoteOff; }
                else if (hi == 0xB0) { ev.Command = CmdCc; }
                else if (hi == 0xC0) { ev.Command = CmdProgram; }
                else if (hi == 0xE0) { ev.Command = CmdPitch; }
                else { ev.Command = CmdOther; }

                EventHandler<MidiEventArgs> hl = MessageReceived;
                if (hl != null) hl(this, ev);
            }
            catch (Exception)
            {
                // Nunca propagar hacia el driver.
            }
        }

        public const string CmdNote = "note";
        public const string CmdNoteOff = "note_off";
        public const string CmdCc = "cc";
        public const string CmdProgram = "program";
        public const string CmdPitch = "pitch";
        public const string CmdOther = "other";

        private void FreeCallback()
        {
            if (_callbackGuard.IsAllocated) _callbackGuard.Free();
            _callback = null;
        }

        public void Close()
        {
            IntPtr h = _handle;
            _handle = IntPtr.Zero;
            if (h != IntPtr.Zero)
            {
                try { midiInStop(h); } catch (Exception) { }
                try { midiInReset(h); } catch (Exception) { }
                try { midiInClose(h); } catch (Exception) { }
            }
            FreeCallback();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Close();
        }
    }
}
