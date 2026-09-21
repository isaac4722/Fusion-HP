// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MidiOut.h : Salida MIDI nativa para Windows via winmm.dll (carga
//  dinamica, sin dependencias). Permite disparar cambios de programa
//  hacia mesas de iluminacion DMX o controladores externos como trigger
//  de eventos de proyeccion. En plataformas no-Windows queda en no-op.
// ============================================================================
#ifndef LUMINA_MIDIOUT_H
#define LUMINA_MIDIOUT_H

#include <QLibrary>
#include <QDebug>

#ifdef Q_OS_WIN
#include <windows.h>
#endif

class MidiOut
{
public:
    MidiOut()
    {
#ifdef Q_OS_WIN
        m_lib.setFileName(QStringLiteral("winmm.dll"));
        if (m_lib.load()) {
            // CORRECCION v1.2.0: midiOutOpen exige 5 parametros (LPHMIDIOUT,
            // UINT, DWORD_PTR, DWORD_PTR, DWORD). La firma anterior pasaba solo
            // 2 -> en x86 (stdcall) el callee limpia 20 bytes de pila cuando el
            // caller solo apilo 8: corrupcion de ESP / crash al activar MIDI.
            using MidiOutOpen_t = MMRESULT (WINAPI *)(LPHMIDIOUT, UINT, DWORD_PTR, DWORD_PTR, DWORD);
            auto open = reinterpret_cast<MidiOutOpen_t>(m_lib.resolve("midiOutOpen"));
            m_short = reinterpret_cast<MMRESULT(WINAPI *)(HMIDIOUT, UINT)>(m_lib.resolve("midiOutShortMsg"));
            if (open) open(&m_handle, 0, 0, 0, 0);   // primer dispositivo MIDI
        }
#endif
    }

    ~MidiOut()
    {
#ifdef Q_OS_WIN
        if (m_handle) {
            auto close = reinterpret_cast<MMRESULT(WINAPI *)(HMIDIOUT)>(m_lib.resolve("midiOutClose"));
            if (close) close(m_handle);
        }
#endif
    }

    bool valid() const
    {
#ifdef Q_OS_WIN
        return m_handle != nullptr && m_short != nullptr;
#else
        return false;
#endif
    }

    // status: 0xC0 program change, 0xB0 control change; data1/data2
    void send(int status, int data1 = 0, int data2 = 0)
    {
#ifdef Q_OS_WIN
        if (valid())
            m_short(m_handle, DWORD((status & 0xFF) | ((data1 & 0x7F) << 8) | ((data2 & 0x7F) << 16)));
#else
        Q_UNUSED(status); Q_UNUSED(data1); Q_UNUSED(data2);
#endif
    }

    void sendProgramChange(int program, int channel = 0)
    {
        send(0xC0 | (channel & 0x0F), program, 0);
    }

    void sendControlChange(int controller, int value, int channel = 0)
    {
        send(0xB0 | (channel & 0x0F), controller, value);
    }

private:
#ifdef Q_OS_WIN
    QLibrary m_lib;
    HMIDIOUT m_handle = nullptr;
    MMRESULT(WINAPI *m_short)(HMIDIOUT, UINT) = nullptr;
#endif
};

#endif // LUMINA_MIDIOUT_H
