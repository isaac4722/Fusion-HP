// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  DisplayEngine.h : Enrutamiento multipantalla independiente.
//  - OutputWindow  : salida limpia de audiencia (fullscreen, crossfade,
//                    capa de video LibVLC para fondos/medios).
//  - DisplayEngine : asignacion de pantallas (QGuiApplication::screens()),
//                    recuperacion ante desconexiones (screenRemoved),
//                    simulacion para equipos de un solo monitor.
// ============================================================================
#ifndef LUMINA_DISPLAYENGINE_H
#define LUMINA_DISPLAYENGINE_H

#include "core/Models.h"
#include "core/Renderer.h"

#include <QObject>
#include <QWidget>
#include <QApplication>
#include <QScreen>
#include <QLabel>
#include <QVariantAnimation>
#include <QTimer>

// ---------------------------------------------------------------------------
// Contenedor nativo para incrustar la salida de video de LibVLC
// ---------------------------------------------------------------------------
class VideoHost : public QWidget
{
    Q_OBJECT
public:
    explicit VideoHost(QWidget *parent = nullptr) : QWidget(parent)
    {
        setAttribute(Qt::WA_NativeWindow, true);
        setAttribute(Qt::WA_DontCreateNativeAncestors, true);
        setAttribute(Qt::WA_OpaquePaintEvent, true);
        setStyleSheet(QStringLiteral("background-color: black; border: none;"));
    }
};

// ---------------------------------------------------------------------------
// Ventana de salida de audiencia
// ---------------------------------------------------------------------------
class OutputWindow : public QWidget
{
    Q_OBJECT
public:
    enum Mode { Black, Clear, Logo, Content };

    explicit OutputWindow(QWidget *parent = nullptr)
        : QWidget(parent, Qt::Window | Qt::FramelessWindowHint | Qt::WindowStaysOnTopHint)
    {
        setAttribute(Qt::WA_OpaquePaintEvent);
        setAttribute(Qt::WA_NoSystemBackground);
        setStyleSheet(QStringLiteral("background: black;"));
        m_videoHost = new VideoHost(this);
        m_videoHost->hide();
        m_mode = Black;
        m_fitMode = 0;
    }

    // ---- Estado ----
    Mode mode() const { return m_mode; }

    // ---- Contenido ----
    void setSlide(const Slide &slide, const Theme &theme)
    {
        stopVideoInternal();
        m_mode = Content;
        m_theme = theme;
        Renderer::Options opt;
        opt.showChords = false;             // audiencia sin cifras
        opt.showTitle = true;
        m_current = Renderer::render(theme, slide, targetSize(), opt);
        if (slide.kind == Slide::Image && !slide.mediaPath.isEmpty())
            m_current = renderImageSlide(slide, theme);
        if (slide.kind == Slide::Blank)
            m_current = Renderer::renderBackground(theme, targetSize());
        m_prev = m_shown;
        startFade();
    }

    void setImage(const QPixmap &pm, const Theme &theme, const QString &label = QString())
    {
        stopVideoInternal();
        m_mode = Content;
        m_theme = theme;
        m_current = composeImage(pm, theme, label);
        m_prev = m_shown;
        startFade();
    }

    void setThemeBackground(const Theme &theme)
    {
        stopVideoInternal();
        m_mode = Content;
        m_theme = theme;
        m_current = Renderer::renderBackground(theme, targetSize());
        m_prev = m_shown;
        startFade();
    }

    void showBlack()  { stopVideoInternal(); m_mode = Black;  m_prev = m_shown; m_current = QPixmap(); startFade(); }
    void showClear()  { stopVideoInternal(); m_mode = Clear;  m_prev = m_shown; m_current = Renderer::renderBackground(m_theme, targetSize()); startFade(); }
    void showLogo(const QPixmap &logo) { stopVideoInternal(); m_mode = Logo; m_logo = logo; m_prev = m_shown; m_current = Renderer::renderLogo(m_theme, logo, targetSize()); startFade(); }

    // ---- Video ----
    QWidget *videoHost() const { return m_videoHost; }
    void setVideoVisible(bool on)
    {
        if (on) {
            m_videoHost->setGeometry(videoRect());
            m_videoHost->show();
            m_videoHost->raise();
        } else {
            m_videoHost->hide();
        }
        update();
    }
    bool isVideoVisible() const { return m_videoHost->isVisible(); }

    // 0=Llenar 1=Ajustar(barras) 2=Centrar
    void setVideoFitMode(int m) { m_fitMode = m; if (m_videoHost->isVisible()) m_videoHost->setGeometry(videoRect()); }
    void relayoutVideo() { if (m_videoHost->isVisible()) m_videoHost->setGeometry(videoRect()); }

    // ---- Transiciones ----
    void setFadeMs(int ms) { m_fadeMs = ms; }

    void resizeTo(const QSize &sz)
    {
        m_targetSize = sz;
        m_shown = QPixmap();        // re-render forzado
        m_current = QPixmap();
    }

signals:
    void outputClicked();

protected:
    void paintEvent(QPaintEvent *) override
    {
        QPainter p(this);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        p.fillRect(rect(), Qt::black);
        if (m_mode == Black && m_current.isNull()) { p.end(); return; }
        const QPixmap &top = m_current.isNull() ? m_shown : m_current;
        if (m_fadeAnim && m_fadeAnim->state() == QVariantAnimation::Running && !m_prev.isNull()) {
            // Crossfade sin allocations por frame: opacidad directa del painter
            // (antes se creaba un QPixmap blended en cada repintado).
            p.drawPixmap(rect(), m_prev);
            p.setOpacity(qreal(m_fadeAnim->currentValue().toReal()));
            p.drawPixmap(rect(), top);
            p.setOpacity(1.0);
        } else if (!top.isNull()) {
            p.drawPixmap(rect(), top);
        }
        p.end();
    }

    void mouseDoubleClickEvent(QMouseEvent *) override { emit outputClicked(); }

private:
    QSize targetSize() const { return m_targetSize.isValid() ? m_targetSize : size(); }

    QPixmap renderImageSlide(const Slide &slide, const Theme &theme)
    {
        QImage img(slide.mediaPath);
        QPixmap pm(targetSize());
        pm.fill(Qt::black);
        QPainter p(&pm);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        if (!img.isNull())
            Renderer::drawImageFit(p, img, pm.rect(), Qt::KeepAspectRatioByExpanding);
        if (!slide.title.isEmpty()) {
            Renderer::drawStyledText(p, theme.title, slide.title,
                                     QRectF(pm.width() * 0.05, pm.height() * 0.86,
                                            pm.width() * 0.9, pm.height() * 0.1), 1.0);
        }
        p.end();
        return pm;
    }

    QPixmap composeImage(const QPixmap &pmIn, const Theme &theme, const QString &label)
    {
        QPixmap pm(targetSize());
        pm.fill(Qt::black);
        QPainter p(&pm);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        if (!pmIn.isNull())
            Renderer::drawImageFit(p, pmIn.toImage(), pm.rect(), Qt::KeepAspectRatio);
        if (!label.isEmpty()) {
            Renderer::drawStyledText(p, theme.title, label,
                                     QRectF(pm.width() * 0.05, pm.height() * 0.86,
                                            pm.width() * 0.9, pm.height() * 0.1), 1.0);
        }
        p.end();
        return pm;
    }

    QRect videoRect() const
    {
        const QRect r = rect();
        if (m_fitMode == 0) return r;                       // Llenar
        if (m_fitMode == 1) {                               // Ajustar (contain)
            QSize s = m_videoSize.isValid() ? m_videoSize : r.size();
            s.scale(r.size(), Qt::KeepAspectRatio);
            return QRect((r.width() - s.width()) / 2, (r.height() - s.height()) / 2, s.width(), s.height());
        }
        // Centrar a tamano nativo (o fit si excede)
        QSize s = m_videoSize.isValid() ? m_videoSize : r.size();
        if (s.width() > r.width() || s.height() > r.height())
            s.scale(r.size(), Qt::KeepAspectRatio);
        return QRect((r.width() - s.width()) / 2, (r.height() - s.height()) / 2, s.width(), s.height());
    }

    void startFade()
    {
        if (m_fadeMs <= 0 || m_prev.isNull() || m_current.isNull()) {
            m_shown = m_current;
            update();
            return;
        }
        if (!m_fadeAnim) {
            m_fadeAnim = new QVariantAnimation(this);
            connect(m_fadeAnim, &QVariantAnimation::valueChanged, this, [this]() { update(); });
            connect(m_fadeAnim, &QVariantAnimation::finished, this, [this]() {
                m_shown = m_current;
                m_prev = QPixmap();
                update();
            });
        }
        m_fadeAnim->stop();
        m_fadeAnim->setDuration(m_fadeMs);
        m_fadeAnim->setStartValue(0.0);
        m_fadeAnim->setEndValue(1.0);
        m_fadeAnim->start();
    }

    void stopVideoInternal() { /* MainWindow coordina el stop por MediaEngine */ }

public:
    QSize m_videoSize;      // resolucion nativa del video (la fija MediaEngine)

private:
    VideoHost *m_videoHost = nullptr;
    QPixmap m_shown, m_current, m_prev;
    QPixmap m_logo;
    Theme m_theme;
    Mode m_mode = Black;
    int m_fadeMs = 250;
    int m_fitMode = 0;
    QSize m_targetSize;
    QVariantAnimation *m_fadeAnim = nullptr;
};

// ---------------------------------------------------------------------------
// Motor de pantallas
// ---------------------------------------------------------------------------
class DisplayEngine : public QObject
{
    Q_OBJECT
public:
    explicit DisplayEngine(QObject *parent = nullptr) : QObject(parent)
    {
        connect(qApp, &QGuiApplication::screenAdded, this, &DisplayEngine::screensChanged);
        connect(qApp, &QGuiApplication::screenRemoved, this, &DisplayEngine::onScreenRemoved);
    }

    QScreen *screenAt(int idx) const
    {
        const QList<QScreen *> screens = QGuiApplication::screens();
        if (idx >= 0 && idx < screens.size()) return screens.at(idx);
        return nullptr;
    }

    // Muestra la salida de audiencia en la pantalla pedida (-1 = principal)
    bool showOutputOn(int screenIdx, OutputWindow *win)
    {
        QScreen *scr = screenAt(screenIdx);
        if (!scr) return false;
        // Geometria directa a la pantalla objetivo (portable en Qt 5.15)
        win->setGeometry(scr->geometry());
        win->showFullScreen();
        return true;
    }

    bool isOutputOn(OutputWindow *win, int screenIdx) const
    {
        QScreen *scr = screenAt(screenIdx);
        if (!scr || !win->isVisible()) return false;
        return win->geometry().center() == scr->geometry().center();
    }

signals:
    void screensChanged();      // reconstruir combos de pantallas en la GUI

private slots:
    void onScreenRemoved(QScreen *)
    {
        // Recuperacion: las ventanas quedan en la principal; la GUI se reordena.
        emit screensChanged();
    }
};

#endif // LUMINA_DISPLAYENGINE_H
