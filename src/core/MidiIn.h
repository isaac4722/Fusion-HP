// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MidiIn.h : Entrada MIDI nativa para Windows via winmm.dll (v1.4.0).
//  Convierte el hardware MIDI (pedales, teclas de control, pads) en eventos
//  de disparo EXTERNOS del motor de triggers — el spec Holyrics define los
//  activadores con eventos internos Y externos ("recibir un comando MIDI").
//
//  Diseno (disciplina cpp-pro):
//   - RAII: el handle HMIDIIN se abre en start() y se cierra SIEMPRE en el
//     destructor / stop() (midiInReset + midiInClose).
//   - Callback estatico de winmm (hilo winmm != hilo Qt): el unico camino
//     seguro hacia el mundo Qt es QMetaObject::invokeMethod con
//     Qt::QueuedConnection — nunca tocar widgets/señales directamente.
//   - Mapa nota->comando editable (JSON persistido por la GUI). Nota 0-127;
//     se ignora velocity 0 (note-off) para no duplicar disparos.
//   - No-Windows: no-op completo (valid() == false), identico a MidiOut.
// ============================================================================
#ifndef LUMINA_MIDIIN_H
#define LUMINA_MIDIIN_H

#include <QObject>
#include <QVector>
#include <QPair>
#include <QString>
#include <QLibrary>
#include <QDebug>

#ifdef Q_OS_WIN
#include <windows.h>
#endif

class MidiIn : public QObject
{
    Q_OBJECT
public:
    using NoteMap = QVector<QPair<int, QString>>;   // (nota MIDI, comando)

    explicit MidiIn(QObject *parent = nullptr) : QObject(parent)
    {
#ifdef Q_OS_WIN
        m_lib.setFileName(QStringLiteral("winmm.dll"));
        m_loaded = m_lib.load();
#endif
    }

    ~MidiIn() override { stop(); }                    // RAII: cierre garantizado

    bool valid() const
    {
#ifdef Q_OS_WIN
        return m_loaded && m_open;
#else
        return false;
#endif
    }

    static int deviceCount()
    {
#ifdef Q_OS_WIN
        QLibrary lib(QStringLiteral("winmm.dll"));
        if (!lib.load()) return 0;
        auto numDevs = reinterpret_cast<UINT(WINAPI *)()>(lib.resolve("midiInGetNumDevs"));
        return numDevs ? int(numDevs()) : 0;
#else
        return 0;
#endif
    }

    // Abre el primer dispositivo MIDI IN con el mapa indicado.
    // Idempotente: reabrir con otro mapa solo sustituye el mapa.
    bool start(const NoteMap &map)
    {
        m_map = map;
#ifdef Q_OS_WIN
        if (!m_loaded) return false;
        if (m_open) return true;
        if (deviceCount() == 0) return false;
        auto open = reinterpret_cast<MMRESULT (WINAPI *)(LPHMIDIIN, UINT, DWORD_PTR, DWORD_PTR, DWORD)>(
                        m_lib.resolve("midiInOpen"));
        if (!open) return false;
        // CALLBACK_FUNCTION: winmm invoca MidiInProc en SU hilo; dwInstance
        // viaja el puntero 'this' para alcanzar el marshalling hacia Qt.
        if (open(&m_handle, 0, reinterpret_cast<DWORD_PTR>(&MidiIn::MidiInProc),
                 reinterpret_cast<DWORD_PTR>(this), CALLBACK_FUNCTION) == MMSYSERR_NOERROR) {
            auto reset   = reinterpret_cast<MMRESULT (WINAPI *)(HMIDIIN)>(m_lib.resolve("midiInReset"));
            auto startFn = reinterpret_cast<MMRESULT (WINAPI *)(HMIDIIN)>(m_lib.resolve("midiInStart"));
            if (reset)   reset(m_handle);     // purga eventos pendientes
            if (startFn) startFn(m_handle);
            m_open = true;
            return true;
        }
        m_handle = nullptr;
        return false;
#else
        return false;
#endif
    }

    void stop()
    {
#ifdef Q_OS_WIN
        if (m_open && m_handle) {
            auto reset = reinterpret_cast<MMRESULT (WINAPI *)(HMIDIIN)>(m_lib.resolve("midiInReset"));
            auto close = reinterpret_cast<MMRESULT (WINAPI *)(HMIDIIN)>(m_lib.resolve("midiInClose"));
            if (reset) reset(m_handle);
            if (close) close(m_handle);
            m_handle = nullptr;
            m_open = false;
        }
#endif
    }

    const NoteMap &map() const { return m_map; }

    // Ruta de prueba/diagnostico: inyecta un evento como si llegara del
    // hardware (lo usa el harness y el boton "Probar" de la GUI).
    void injectNote(int note, int velocity)
    {
        if (note < 0 || note > 127) return;
        if (velocity <= 0) return;                     // note-off: ignorar
        dispatch(note);
    }

signals:
    // Comando listo para el dispatcher central (mismo vocabulario que el
    // control remoto web: next/prev/black/clear/logo/qnext/qprev/...).
    void midiCommand(const QString &cmd);

private:
    // Q_INVOKABLE: imprescindible para QMetaObject::invokeMethod por nombre
    // desde el callback estatico de winmm (hilo distinto).
    Q_INVOKABLE void dispatch(int note)
    {
        for (const auto &p : m_map) {
            if (p.first == note && !p.second.isEmpty()) {
                emit midiCommand(p.second);
                return;                                 // primera coincidencia
            }
        }
    }

#ifdef Q_OS_WIN
    static void CALLBACK MidiInProc(HMIDIIN, UINT wMsg, DWORD_PTR dwInstance,
                                    DWORD_PTR dwParam1, DWORD_PTR)
    {
        if (wMsg != MIM_DATA) return;                  // 0x3C3: short message
        MidiIn *self = reinterpret_cast<MidiIn *>(dwInstance);
        if (!self) return;
        const int status   = int(dwParam1 & 0xFF);
        const int data1    = int((dwParam1 >> 8) & 0x7F);   // nota
        const int data2    = int((dwParam1 >> 16) & 0x7F);  // velocity
        if ((status & 0xF0) != 0x90) return;           // solo Note On
        if (data2 == 0) return;                        // velocity 0 = note-off
        // Hilo winmm -> hilo Qt: QUEUED es obligatorio (emit aqui seria
        // cross-thread sobre objetos no thread-safe).
        QMetaObject::invokeMethod(self, "dispatch", Qt::QueuedConnection,
                                  Q_ARG(int, data1));
    }
#endif

private:
#ifdef Q_OS_WIN
    QLibrary m_lib;
    bool     m_loaded = false;
    bool     m_open = false;
    HMIDIIN  m_handle = nullptr;
#endif
    NoteMap  m_map;
};

#endif // LUMINA_MIDIIN_H
