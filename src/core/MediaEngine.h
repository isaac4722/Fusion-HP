// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MediaEngine.h : Integracion nativa LibVLC 3.x con CARGA DINAMICA.
//  El ejecutable nunca enlaza estaticamente a libvlc: resuelve los simbolos
//  en runtime desde <appdir>/vlc/libvlc.dll (o libvlc.so.5). Si VLC no esta,
//  el motor degrada con elegancia y la proyeccion de texto sigue operando.
//  Decodificacion por GPU (DXVA2/D3D11VA) en hilo aparte del GUI Thread.
// ============================================================================
#ifndef LUMINA_MEDIAENGINE_H
#define LUMINA_MEDIAENGINE_H

#include <QObject>
#include <QLibrary>
#include <QTimer>
#include <QCoreApplication>
#include <QWidget>
#include <QDir>
#include <QUrl>
#include <QDebug>

#include <cstdio>
#include <cstdint>

// Tipos opacos minimos de la API C de libvlc 3.x
typedef struct libvlc_instance_t libvlc_instance_t;
typedef struct libvlc_media_t libvlc_media_t;
typedef struct libvlc_media_player_t libvlc_media_player_t;

class MediaEngine : public QObject
{
    Q_OBJECT
public:
    enum State { Nothing = 0, Opening, Buffering, Playing, Paused, Stopped, Ended, Error };

    explicit MediaEngine(QObject *parent = nullptr) : QObject(parent)
    {
        m_poll = new QTimer(this);
        m_poll->setInterval(300);
        connect(m_poll, &QTimer::timeout, this, &MediaEngine::pollState);
    }

    ~MediaEngine() override { shutdown(); }

    bool available() const { return m_ok; }
    QString version() const { return m_version; }

    bool initialize(QString *error = nullptr)
    {
        if (m_ok) return true;

        // 1) Resolver libreria: primero junto al ejecutable (portable), luego sistema
        QString appDir = QCoreApplication::applicationDirPath();
        QStringList candidates;
#ifdef Q_OS_WIN
        qputenv("VLC_PLUGIN_PATH", QDir::toNativeSeparators(appDir + QStringLiteral("/vlc/plugins")).toUtf8());
        candidates << appDir + QStringLiteral("/vlc/libvlc.dll")
                   << QStringLiteral("libvlc.dll");
#else
        candidates << appDir + QStringLiteral("/vlc/lib/libvlc.so.5")
                   << appDir + QStringLiteral("/vlc/libvlc.so.5")
                   << QStringLiteral("libvlc.so.5");
#endif
        QLibrary lib;
        for (const QString &c : candidates) {
            lib.setFileName(c);
            if (lib.load()) break;
        }
        if (!lib.isLoaded()) {
            if (error) *error = QStringLiteral("LibVLC no encontrada (carpeta 'vlc/' junto al ejecutable).");
            return false;
        }

        // 2) Resolver simbolos esenciales
#define LUMINA_SYM(name) \
        if (!(p_##name = reinterpret_cast<decltype(p_##name)>(lib.resolve(#name)))) { \
            if (error) *error = QStringLiteral("Simbolo faltante en LibVLC: ") + QStringLiteral(#name); \
            return false; \
        }
        LUMINA_SYM(libvlc_new)
        LUMINA_SYM(libvlc_release)
        LUMINA_SYM(libvlc_media_new_location)
        LUMINA_SYM(libvlc_media_new_path)
        LUMINA_SYM(libvlc_media_release)
        LUMINA_SYM(libvlc_media_add_option)
        LUMINA_SYM(libvlc_media_player_new_from_media)
        LUMINA_SYM(libvlc_media_player_release)
        LUMINA_SYM(libvlc_media_player_play)
        LUMINA_SYM(libvlc_media_player_pause)
        LUMINA_SYM(libvlc_media_player_stop)
        LUMINA_SYM(libvlc_media_player_set_hwnd)
        LUMINA_SYM(libvlc_media_player_set_xwindow)
        LUMINA_SYM(libvlc_media_player_get_state)
        LUMINA_SYM(libvlc_media_player_get_time)
        LUMINA_SYM(libvlc_media_player_set_time)
        LUMINA_SYM(libvlc_media_player_get_length)
        LUMINA_SYM(libvlc_media_player_is_seekable)
        LUMINA_SYM(libvlc_audio_set_volume)
        LUMINA_SYM(libvlc_audio_get_volume)
        LUMINA_SYM(libvlc_video_get_size)
#undef LUMINA_SYM

        // 3) Instancia VLC (1 instancia, 2 reproductores: principal + fondo)
        const char *argv[] = { "--no-video-title-show", "--quiet" };
        m_vlc = p_libvlc_new(2, argv);
        if (!m_vlc) {
            if (error) *error = QStringLiteral("libvlc_new fallo.");
            return false;
        }
        m_ok = true;
        m_version = QStringLiteral("LibVLC 3.x (embebida)");
        qInfo() << "[Media] LibVLC cargada dinamicamente OK";
        return true;
    }

    void shutdown()
    {
        stopMain(); stopBackground();
        if (m_vlc && p_libvlc_release) { p_libvlc_release(m_vlc); }
        m_vlc = nullptr;
        m_ok = false;
    }

    // ------------------------- Reproduccion principal ------------------------
    // Reproduce video/audio en la ventana de salida (videoHost) o solo audio.
    bool playMain(const QString &path, QWidget *videoWidget, bool loop, bool isVideo)
    {
        if (!m_ok) return false;
        stopMain();
        libvlc_media_t *md = createMedia(path, loop);
        if (!md) return false;
        m_main = p_libvlc_media_player_new_from_media(md);
        p_libvlc_media_release(md);
        if (!m_main) return false;
        if (isVideo && videoWidget) {
            attachToWindow(m_main, reinterpret_cast<void *>(videoWidget->winId()));
        }
        p_libvlc_audio_set_volume(m_main, m_volume);
        p_libvlc_media_player_play(m_main);
        m_state = Opening;
        m_poll->start();
        return true;
    }

    void pauseMain(bool on)
    {
        if (m_main && p_libvlc_media_player_pause) p_libvlc_media_player_pause(m_main);
        Q_UNUSED(on)
    }

    void stopMain()
    {
        if (m_main) {
            p_libvlc_media_player_stop(m_main);
            p_libvlc_media_player_release(m_main);
            m_main = nullptr;
        }
        m_state = Stopped;
        m_poll->stop();
    }

    void seekMain(qint64 ms)
    {
        if (m_main) p_libvlc_media_player_set_time(m_main, ms);
    }

    bool mainSeekable() const { return m_main ? p_libvlc_media_player_is_seekable(m_main) : false; }
    qint64 mainTime() const { return m_main ? p_libvlc_media_player_get_time(m_main) : 0; }
    qint64 mainLength() const { return m_main ? p_libvlc_media_player_get_length(m_main) : 0; }
    State state() const { return m_state; }

    QSize nativeVideoSize() const
    {
        unsigned w = 0, h = 0;
        if (m_main && p_libvlc_video_get_size && p_libvlc_video_get_size(m_main, 0, &w, &h) == 0)
            return QSize(int(w), int(h));
        return QSize();
    }

    // ------------------------- Fondo de video en bucle -----------------------
    bool playBackground(const QString &path)
    {
        if (!m_ok) return false;
        stopBackground();
        libvlc_media_t *md = createMedia(path, true);
        if (!md) return false;
        m_bg = p_libvlc_media_player_new_from_media(md);
        p_libvlc_media_release(md);
        if (!m_bg) return false;
        p_libvlc_audio_set_volume(m_bg, 0);     // fondo sin audio
        return true;
    }

    void attachBackground(QWidget *w)
    {
        if (m_bg && w) attachToWindow(m_bg, reinterpret_cast<void *>(w->winId()));
    }

    void startBackground()
    {
        if (m_bg) { p_libvlc_media_player_play(m_bg); }
    }

    void stopBackground()
    {
        if (m_bg) {
            p_libvlc_media_player_stop(m_bg);
            p_libvlc_media_player_release(m_bg);
            m_bg = nullptr;
        }
    }

    // ------------------------------ Volumen ---------------------------------
    void setVolume(int vol)     // 0..100
    {
        m_volume = vol;
        if (m_main) p_libvlc_audio_set_volume(m_main, vol);
    }
    int volume() const { return m_volume; }

signals:
    void stateChanged(int state);
    void positionChanged(qint64 timeMs, qint64 lengthMs);
    void finished();

private slots:
    void pollState()
    {
        if (!m_main) return;
        const int s = p_libvlc_media_player_get_state(m_main);
        const State ns = static_cast<State>(s);
        if (ns != m_state) {
            m_state = ns;
            emit stateChanged(int(ns));
            if (ns == Ended || ns == Error) {
                m_poll->stop();
                emit finished();
            }
        }
        emit positionChanged(p_libvlc_media_player_get_time(m_main),
                             p_libvlc_media_player_get_length(m_main));
    }

private:
    libvlc_media_t *createMedia(const QString &path, bool loop)
    {
        libvlc_media_t *md = nullptr;
        const QByteArray u8 = path.toUtf8();
        if (path.startsWith(QStringLiteral("http://")) || path.startsWith(QStringLiteral("https://")) ||
            path.startsWith(QStringLiteral("rtsp://")) || path.startsWith(QStringLiteral("mms://"))) {
            md = p_libvlc_media_new_location(m_vlc, u8.constData());
        } else {
#ifdef Q_OS_WIN
            // location con file:// garantiza soporte UTF-8 en Windows
            const QString loc = QUrl::fromLocalFile(path).toString();
            md = p_libvlc_media_new_location(m_vlc, loc.toUtf8().constData());
#else
            md = p_libvlc_media_new_path(m_vlc, u8.constData());
#endif
        }
        if (md && loop)
            p_libvlc_media_add_option(md, ":input-repeat=65535");
        return md;
    }

    void attachToWindow(libvlc_media_player_t *mp, void *winId)
    {
#ifdef Q_OS_WIN
        p_libvlc_media_player_set_hwnd(mp, winId);
#else
        p_libvlc_media_player_set_xwindow(mp, static_cast<uint32_t>(reinterpret_cast<uintptr_t>(winId)));
#endif
    }

    // Punteros a funciones LibVLC
    libvlc_instance_t *(*p_libvlc_new)(int, const char *const *) = nullptr;
    void (*p_libvlc_release)(libvlc_instance_t *) = nullptr;
    libvlc_media_t *(*p_libvlc_media_new_location)(libvlc_instance_t *, const char *) = nullptr;
    libvlc_media_t *(*p_libvlc_media_new_path)(libvlc_instance_t *, const char *) = nullptr;
    void (*p_libvlc_media_release)(libvlc_media_t *) = nullptr;
    void (*p_libvlc_media_add_option)(libvlc_media_t *, const char *) = nullptr;
    libvlc_media_player_t *(*p_libvlc_media_player_new_from_media)(libvlc_media_t *) = nullptr;
    void (*p_libvlc_media_player_release)(libvlc_media_player_t *) = nullptr;
    int (*p_libvlc_media_player_play)(libvlc_media_player_t *) = nullptr;
    void (*p_libvlc_media_player_pause)(libvlc_media_player_t *) = nullptr;
    void (*p_libvlc_media_player_stop)(libvlc_media_player_t *) = nullptr;
    void (*p_libvlc_media_player_set_hwnd)(libvlc_media_player_t *, void *) = nullptr;
    void (*p_libvlc_media_player_set_xwindow)(libvlc_media_player_t *, uint32_t) = nullptr;
    int (*p_libvlc_media_player_get_state)(libvlc_media_player_t *) = nullptr;
    qint64 (*p_libvlc_media_player_get_time)(libvlc_media_player_t *) = nullptr;
    void (*p_libvlc_media_player_set_time)(libvlc_media_player_t *, qint64) = nullptr;
    qint64 (*p_libvlc_media_player_get_length)(libvlc_media_player_t *) = nullptr;
    int (*p_libvlc_media_player_is_seekable)(libvlc_media_player_t *) = nullptr;
    int (*p_libvlc_audio_set_volume)(libvlc_media_player_t *, int) = nullptr;
    int (*p_libvlc_audio_get_volume)(libvlc_media_player_t *) = nullptr;
    int (*p_libvlc_video_get_size)(libvlc_media_player_t *, unsigned, unsigned *, unsigned *) = nullptr;

    libvlc_instance_t *m_vlc = nullptr;
    libvlc_media_player_t *m_main = nullptr;
    libvlc_media_player_t *m_bg = nullptr;
    QTimer *m_poll = nullptr;
    State m_state = Stopped;
    int m_volume = 90;
    bool m_ok = false;
    QString m_version;
};

#endif // LUMINA_MEDIAENGINE_H
