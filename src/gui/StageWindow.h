// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  StageWindow.h : Monitor de retorno (Stage View) para musicos.
//  Alto contraste: reloj, estrofa actual grande, vista previa siguiente,
//  cifras/acordes encima del texto, temporizador y alertas superpuestas.
// ============================================================================
#ifndef LUMINA_STAGEWINDOW_H
#define LUMINA_STAGEWINDOW_H

#include "core/Models.h"

#include <QWidget>
#include <QLabel>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QTimer>
#include <QTime>
#include <QDateTime>

class StageWindow : public QWidget
{
    Q_OBJECT
public:
    explicit StageWindow(QWidget *parent = nullptr)
        : QWidget(parent, Qt::Window | Qt::FramelessWindowHint)
    {
        setStyleSheet(QStringLiteral("background-color: #000000;"));
        setAttribute(Qt::WA_OpaquePaintEvent);

        auto *main = new QVBoxLayout(this);
        main->setContentsMargins(24, 16, 24, 16);
        main->setSpacing(10);

        // --- Barra superior: reloj / tono / bpm ---
        auto *top = new QHBoxLayout();
        m_clock = new QLabel(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")), this);
        m_keyBpm = new QLabel(QString(), this);
        m_alert = new QLabel(QString(), this);
        QString topStyle = QStringLiteral("font-size: 22pt; font-weight: bold;");
        m_clock->setStyleSheet(topStyle + QStringLiteral("color: #00FF66;"));
        m_keyBpm->setStyleSheet(topStyle + QStringLiteral("color: #5588FF;"));
        m_alert->setStyleSheet(QStringLiteral("font-size: 20pt; color: #FFD700; font-weight: bold;"));
        m_alert->setWordWrap(true);
        top->addWidget(m_clock);
        top->addWidget(m_keyBpm);
        top->addStretch();
        top->addWidget(m_alert, 3);
        main->addLayout(top);

        // --- Titulo cancion / etiqueta de seccion ---
        m_title = new QLabel(QString(), this);
        m_title->setStyleSheet(QStringLiteral("font-size: 18pt; color: #88BBFF;"));
        main->addWidget(m_title);

        // --- Estrofa actual (grande, con acordes) ---
        m_current = new QLabel(QStringLiteral("—"), this);
        m_current->setStyleSheet(QStringLiteral("font-size: 34pt; color: #FFFFFF; font-weight: bold;"));
        m_current->setWordWrap(true);
        m_current->setAlignment(Qt::AlignCenter);
        main->addWidget(m_current, 5);

        // --- Cuenta regresiva ---
        m_countdown = new QLabel(QString(), this);
        m_countdown->setStyleSheet(QStringLiteral("font-size: 26pt; color: #FFAA33; font-weight: bold;"));
        m_countdown->setAlignment(Qt::AlignCenter);
        main->addWidget(m_countdown);

        // --- Siguiente estrofa ---
        m_next = new QLabel(QStringLiteral("Siguiente: —"), this);
        m_next->setStyleSheet(QStringLiteral("font-size: 20pt; color: #999999; font-style: italic;"));
        m_next->setWordWrap(true);
        m_next->setAlignment(Qt::AlignCenter);
        main->addWidget(m_next, 2);

        // Timers
        auto *clockTimer = new QTimer(this);
        connect(clockTimer, &QTimer::timeout, this, [this]() {
            m_clock->setText(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")));
            if (m_countdownActive && m_deadline.isValid()) {
                const qint64 secs = QDateTime::currentDateTime().secsTo(m_deadline);
                if (secs > 0) {
                    m_countdown->setText(QStringLiteral("Temporizador: %1").arg(
                        QTime(0, 0).addSecs(int(secs)).toString(QStringLiteral("hh:mm:ss"))));
                    if (secs <= 60)
                        m_countdown->setStyleSheet(QStringLiteral("font-size: 26pt; color: #FF5555; font-weight: bold;"));
                } else {
                    m_countdown->setText(QStringLiteral("¡TIEMPO!"));
                    m_countdownActive = false;
                }
            }
        });
        clockTimer->start(1000);

        auto *flashTimer = new QTimer(this);
        connect(flashTimer, &QTimer::timeout, this, [this]() {
            if (m_alert->text().isEmpty()) return;
            m_alertVisible = !m_alertVisible;
            m_alert->setStyleSheet(m_alertVisible
                ? QStringLiteral("font-size: 20pt; color: #FFD700; font-weight: bold;")
                : QStringLiteral("font-size: 20pt; color: rgba(255,215,0,60); font-weight: bold;"));
        });
        flashTimer->start(700);
    }

    void setThemeColors(const Theme &t)
    {
        m_current->setStyleSheet(QStringLiteral("font-size: 34pt; color: %1; font-weight: bold;")
                                     .arg(t.stageText.name()));
        m_next->setStyleSheet(QStringLiteral("font-size: 20pt; color: %1; font-style: italic;")
                                  .arg(t.stageNext.name()));
    }

    void updateSlide(const Slide &cur, const Slide *next, const Theme &theme)
    {
        Q_UNUSED(theme)
        m_title->setText(cur.title.isEmpty() ? cur.refLabel
                                             : cur.title + (cur.refLabel.isEmpty() ? QString()
                                                                 : QStringLiteral("  —  ") + cur.refLabel));
        QString body;
        for (const SlideLine &l : cur.lines) {
            if (!l.chords.isEmpty())
                body += QStringLiteral("<span style=\"color:#7DE87D; font-size:60%;\">%1</span><br>").arg(l.chords.toHtmlEscaped());
            body += l.text.toHtmlEscaped() + QStringLiteral("<br>");
        }
        if (!cur.notes.isEmpty())
            body += QStringLiteral("<span style=\"color:#FFAA33; font-size:70%;\">[%1]</span>")
                        .arg(cur.notes.toHtmlEscaped());
        m_current->setTextFormat(Qt::RichText);
        m_current->setText(body);

        if (next) {
            QString nb;
            for (const SlideLine &l : next->lines)
                nb += (nb.isEmpty() ? QString() : QStringLiteral(" / ")) + l.text;
            m_next->setText(QStringLiteral("Siguiente: %1").arg(nb));
        } else {
            m_next->setText(QStringLiteral("Siguiente: —"));
        }
    }

    void clearSlide()
    {
        m_title->setText(QString());
        m_current->setText(QStringLiteral("—"));
        m_next->setText(QStringLiteral("Siguiente: —"));
    }

    void setKeyBpm(const QString &text) { m_keyBpm->setText(text); }
    void showAlert(const QString &text)
    {
        m_alert->setText(text);
        QTimer::singleShot(15000, this, [this]() { m_alert->setText(QString()); });
    }

    void startCountdown(int minutes)
    {
        m_deadline = QDateTime::currentDateTime().addSecs(minutes * 60);
        m_countdownActive = true;
        m_countdown->setStyleSheet(QStringLiteral("font-size: 26pt; color: #FFAA33; font-weight: bold;"));
    }
    void stopCountdown() { m_countdownActive = false; m_countdown->setText(QString()); }

private:
    QLabel *m_clock = nullptr;
    QLabel *m_keyBpm = nullptr;
    QLabel *m_alert = nullptr;
    QLabel *m_title = nullptr;
    QLabel *m_current = nullptr;
    QLabel *m_next = nullptr;
    QLabel *m_countdown = nullptr;
    bool m_countdownActive = false;
    bool m_alertVisible = true;
    QDateTime m_deadline;
};

#endif // LUMINA_STAGEWINDOW_H
