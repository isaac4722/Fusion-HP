// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  DirectorWindow.h : Pantalla 3 "HTML / Instrucciones" del Stage View
// (spec §3.3): mensajes y notas internas para el director del servicio.
// Salida independiente (pantalla propia seleccionable) con: reloj y
// temporizador, ítem actual + texto, SIGUIENTE ítem (título y preview),
// notas del ítem y registro persistente de mensajes del operador.
// Contraste alto (visualización a ~1,5 m — tabla embebido: >=20 px).
// ============================================================================
#ifndef LUMINA_DIRECTORWINDOW_H
#define LUMINA_DIRECTORWINDOW_H

#include "core/Models.h"

#include <QWidget>
#include <QLabel>
#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QTimer>
#include <QTime>
#include <QDateTime>

class DirectorWindow : public QWidget
{
    Q_OBJECT
public:
    explicit DirectorWindow(QWidget *parent = nullptr)
        : QWidget(parent, Qt::Window | Qt::FramelessWindowHint)
    {
        setStyleSheet(QStringLiteral("background-color: #10141C;"));
        setAttribute(Qt::WA_OpaquePaintEvent);

        auto *main = new QVBoxLayout(this);
        main->setContentsMargins(28, 20, 28, 20);
        main->setSpacing(10);

        // --- Barra superior: reloj + temporizador ---
        auto *top = new QHBoxLayout();
        m_clock = new QLabel(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")), this);
        m_clock->setStyleSheet(QStringLiteral("font-size: 24pt; color: #FFD166; font-weight: bold;"));
        top->addWidget(m_clock);
        top->addStretch();
        m_countdown = new QLabel(QString(), this);
        m_countdown->setStyleSheet(QStringLiteral("font-size: 22pt; color: #FF5555; font-weight: bold;"));
        top->addWidget(m_countdown);
        main->addLayout(top);

        // --- Ítem actual (título, destacado) ---
        m_item = new QLabel(QStringLiteral("—"), this);
        m_item->setStyleSheet(QStringLiteral("font-size: 26pt; color: #6EC1FF; font-weight: bold;"));
        m_item->setWordWrap(true);
        main->addWidget(m_item);

        // --- Texto de la slide actual (mediano: el director lee guiado) ---
        m_text = new QLabel(QString(), this);
        m_text->setStyleSheet(QStringLiteral("font-size: 17pt; color: #E8ECF4;"));
        m_text->setWordWrap(true);
        main->addWidget(m_text, 4);

        // --- Notas del ítem ---
        m_notes = new QLabel(QString(), this);
        m_notes->setStyleSheet(QStringLiteral("font-size: 14pt; color: #B8C0CC; font-style: italic;"));
        m_notes->setWordWrap(true);
        main->addWidget(m_notes);

        // --- SIGUIENTE ítem (panel diferenciado) ---
        auto *nextBox = new QWidget(this);
        nextBox->setStyleSheet(QStringLiteral(
            "background-color: #1A2230; border-radius: 8px;"));
        auto *nl = new QVBoxLayout(nextBox);
        nl->setContentsMargins(16, 10, 16, 10);
        m_nextTitle = new QLabel(QStringLiteral("SIGUIENTE —"), nextBox);
        m_nextTitle->setStyleSheet(QStringLiteral("font-size: 19pt; color: #9BE564; font-weight: bold;"));
        m_nextTitle->setWordWrap(true);
        nl->addWidget(m_nextTitle);
        m_nextText = new QLabel(QString(), nextBox);
        m_nextText->setStyleSheet(QStringLiteral("font-size: 15pt; color: #B8C0CC;"));
        m_nextText->setWordWrap(true);
        nl->addWidget(m_nextText);
        main->addWidget(nextBox, 2);

        // --- Mensajes del operador (registro persistente, últimos 4) ---
        m_messages = new QLabel(QString(), this);
        m_messages->setStyleSheet(QStringLiteral(
            "font-size: 14pt; color: #FFD166;"));
        m_messages->setWordWrap(true);
        m_messages->setAlignment(Qt::AlignLeft | Qt::AlignTop);
        main->addWidget(m_messages, 2);

        auto *clockTimer = new QTimer(this);
        connect(clockTimer, &QTimer::timeout, this, [this]() {
            m_clock->setText(QTime::currentTime().toString(QStringLiteral("hh:mm:ss")));
            if (m_countdownActive && m_deadline.isValid()) {
                const qint64 secs = QDateTime::currentDateTimeUtc().secsTo(m_deadline);
                if (secs > 0) {
                    m_countdown->setText(QStringLiteral("Temporizador: %1").arg(
                        QTime(0, 0).addSecs(int(secs)).toString(QStringLiteral("hh:mm:ss"))));
                } else {
                    m_countdown->setText(QStringLiteral("¡TIEMPO!"));
                    m_countdownActive = false;
                }
            }
        });
        clockTimer->start(1000);
    }

    // Estado completo del director (lo publica MainWindow al cambiar slide)
    void updateInfo(const QString &itemTitle, const QString &slideText,
                    const QString &nextItemTitle, const QString &nextSlideText,
                    const QString &notes)
    {
        m_item->setText(itemTitle.isEmpty() ? QStringLiteral("—") : itemTitle);
        m_text->setText(slideText);
        m_notes->setText(notes.isEmpty() ? QString()
                        : QStringLiteral("Nota: %1").arg(notes));
        m_nextTitle->setText(nextItemTitle.isEmpty() ? QStringLiteral("SIGUIENTE —")
                        : QStringLiteral("SIGUIENTE · %1").arg(nextItemTitle));
        m_nextText->setText(nextSlideText);
    }

    void clearInfo()
    {
        m_item->setText(QStringLiteral("—"));
        m_text->setText(QString());
        m_notes->setText(QString());
        m_nextTitle->setText(QStringLiteral("SIGUIENTE —"));
        m_nextText->setText(QString());
    }

    // Mensaje del operador: queda registrado (historial visible), no expira
    void addMessage(const QString &text, const QString &title)
    {
        m_history.prepend(QStringLiteral("[%1] %2 — %3")
                .arg(QTime::currentTime().toString(QStringLiteral("hh:mm")),
                     title.isEmpty() ? QStringLiteral("Mensaje") : title,
                     text));
        while (m_history.size() > 4)
            m_history.removeLast();
        QString html;
        for (const QString &m : qAsConst(m_history))
            html += (html.isEmpty() ? QString() : QStringLiteral("<br>")) +
                    m.toHtmlEscaped();
        m_messages->setTextFormat(Qt::RichText);
        m_messages->setText(html);
    }

    void startCountdown(int minutes)
    {
        m_deadline = QDateTime::currentDateTimeUtc().addSecs(minutes * 60);
        m_countdownActive = true;
    }
    void stopCountdown() { m_countdownActive = false; m_countdown->setText(QString()); }

    void applyAccent(const QColor &accent)
    {
        m_item->setStyleSheet(QStringLiteral("font-size: 26pt; color: %1; font-weight: bold;")
                                  .arg(accent.name()));
    }

private:
    QLabel *m_clock = nullptr;
    QLabel *m_countdown = nullptr;
    QLabel *m_item = nullptr;
    QLabel *m_text = nullptr;
    QLabel *m_notes = nullptr;
    QLabel *m_nextTitle = nullptr;
    QLabel *m_nextText = nullptr;
    QLabel *m_messages = nullptr;
    QStringList m_history;
    bool m_countdownActive = false;
    QDateTime m_deadline;
};

#endif // LUMINA_DIRECTORWINDOW_H
