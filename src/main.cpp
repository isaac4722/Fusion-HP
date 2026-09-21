// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  main.cpp : Punto de entrada con salvaguardas de renderizado.
//  - High-DPI scaling para pantallas 4K.
//  - AA_UseOpenGLES (ANGLE: OpenGL ES -> DirectX 9/11) para compatibilidad
//    con GPUs legacy de Windows 7 (evita "LoadLibrary failed 126").
//  - V-Sync forzado para evitar tearing en proyeccion.
//  - Recuperacion ante fallo de contexto GPU (flag software render).
//  - Primer arranque: crea vault SQLite, importa Biblia RVR1909 (dominio
//    publico) y canciones de ejemplo.
// ============================================================================
#include <QApplication>
#include <QCoreApplication>
#include <QSurfaceFormat>
#include <QStandardPaths>
#include <QDir>
#include <QFile>
#include <QFont>
#include <QStyleFactory>
#include <QPalette>
#include <QCommandLineParser>
#include <QLoggingCategory>

#include "core/Database.h"
#include "core/MediaEngine.h"
#include "net/WebServer.h"
#include "core/Triggers.h"
#include "gui/MainWindow.h"

#include <QFileInfo>
#include <QDateTime>
#include <QMessageBox>
#include <cstdio>

static QFile g_logFile;

static void luminaMessageHandler(QtMsgType type, const QMessageLogContext &, const QString &msg)
{
    const QString line = QStringLiteral("[%1] %2\n")
            .arg(QDateTime::currentDateTime().toString(QStringLiteral("hh:mm:ss.zzz")), msg);
    if (g_logFile.isOpen()) { g_logFile.write(line.toUtf8()); g_logFile.flush(); }
    std::fprintf(stderr, "%s", line.toUtf8().constData());
}

// ---------------------------------------------------------------------------
// Primer arranque: seed de base de datos con recursos embebidos
// ---------------------------------------------------------------------------
static void seedDatabase(Database *db)
{
    // Biblia RVR1909 (dominio publico) embebida en el ejecutable
    QString err;
    if (!db->importBibleFromJsonResource(QStringLiteral(":/data/bible_rvr1909.json"),
                                         QStringLiteral("RVR1909"),
                                         QStringLiteral("Reina-Valera 1909 - Dominio público"), &err)) {
        qWarning() << "[Seed] Biblia:" << err;
    }
    // Canciones de ejemplo (hymnos de dominio publico) si el vault esta vacio
    if (db->scalar(QStringLiteral("SELECT COUNT(*) FROM songs")) == 0) {
        const QStringList demo = {
            // titulo|autor|tono|bpm|letra
            QStringLiteral(
                "Sublime Gracia|John Newton (1779)|Sol|72|\n"
                "[Verso 1]\nSol        Do   Sol\nSublime gracia, dulce son\nEm              Do      Sol\n"
                "Que me salvó a un pecador\n\n[Coro]\nDo    Sol\nGloria a Dios en lo más alto\n"
                "Em            Do       Sol\nMi redentor me rescató\n"),
            QStringLiteral(
                "Santo, Santo, Santo|Reginald Heber (1826)|Do|84|\n"
                "[Verso 1]\nDo                Sol\nSanto, santo, santo, Dios Todopoderoso\n"
                "Do                     Sol      Do\nSiempre te cantamos los que aquí estás\n\n"
                "[Coro]\nSol      Do\nSanto es el Señor\n"),
            QStringLiteral(
                "Cristo Ya Nació En Belén|Villancico tradicional|Do|110|\n"
                "[Verso 1]\nDo\nCristo ya nació en Belén\nSol                 Do\n"
                "y en el portal nos lo dan\n\n[Coro]\nDo      Sol\nAleluya, aleluya\n"
                "Sol      Do\nAleluya, aleluya\n"),
            QStringLiteral(
                "Grande Es El Señor|Salmo 48:1|Mi|76|\n"
                "[Verso 1]\nMi          Si\nGrande es el Señor\nMi             Si\n"
                "y digno de suprema alabanza\n\n[Coro]\nSi     Mi\nEn la ciudad de nuestro Dios\n"),
            QStringLiteral(
                "Alabad Al Señor|Salmo 117|La|96|\n"
                "[Verso 1]\nLa         Re\nAlabad al Señor todas las naciones\n"
                "Re        La\nTodos los pueblos le alaben\n\n[Coro]\nRe    La\nPorque para siempre\n"
                "La        Re\nSu misericordia es sobre nosotros\n")
        };
        for (const QString &raw : demo) {
            Song s;
            const QStringList parts = raw.split(QChar('|'));
            if (parts.size() >= 5) {
                s.title = parts.at(0);
                s.artist = parts.at(1);
                s.key = parts.at(2);
                s.bpm = parts.at(3).toInt();
                s.lyrics = parts.at(4).trimmed();
            }
            db->addSong(s);
        }
        qInfo() << "[Seed] Canciones de ejemplo creadas:" << demo.size();
    }
}

int main(int argc, char *argv[])
{
    // ---- Salvaguardas de renderizado (antes de QApplication) ----
    QCoreApplication::setAttribute(Qt::AA_EnableHighDpiScaling);
    QCoreApplication::setAttribute(Qt::AA_UseHighDpiPixmaps);
    // ANGLE: traduce OpenGL ES a DirectX para GPUs legacy Win7 (per spec)
    QCoreApplication::setAttribute(Qt::AA_UseOpenGLES);

    QSurfaceFormat format;
    format.setRenderableType(QSurfaceFormat::OpenGLES);
    format.setSwapInterval(1);          // V-Sync: evita tearing
    QSurfaceFormat::setDefaultFormat(format);

    QApplication app(argc, argv);
    QApplication::setApplicationName(QStringLiteral("LuminaPresentationSuite"));
    QApplication::setOrganizationName(QStringLiteral("LuminaSoftware"));
    QApplication::setApplicationVersion(QStringLiteral("1.1.0"));
    QApplication::setStyle(QStyleFactory::create(QStringLiteral("Fusion")));

    // Paleta oscura profesional
    QPalette pal;
    pal.setColor(QPalette::Window, QColor(24, 30, 48));
    pal.setColor(QPalette::WindowText, QColor(220, 228, 245));
    pal.setColor(QPalette::Base, QColor(16, 21, 36));
    pal.setColor(QPalette::AlternateBase, QColor(22, 29, 48));
    pal.setColor(QPalette::Text, QColor(220, 228, 245));
    pal.setColor(QPalette::Button, QColor(32, 40, 62));
    pal.setColor(QPalette::ButtonText, QColor(220, 228, 245));
    pal.setColor(QPalette::Highlight, QColor(30, 111, 217));
    pal.setColor(QPalette::HighlightedText, Qt::white);
    app.setPalette(pal);

    // Log a archivo (se puede desactivar con LUMINA_NO_LOGFILE=1 para depurar)
    const QString dataDirStr = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    QDir().mkpath(dataDirStr);
    if (!qEnvironmentVariableIsSet("LUMINA_NO_LOGFILE")) {
        g_logFile.setFileName(dataDirStr + QStringLiteral("/lumina.log"));
        if (g_logFile.open(QIODevice::WriteOnly | QIODevice::Append | QIODevice::Text))
            qInstallMessageHandler(luminaMessageHandler);
    }

    // ---- Base de datos ----
    Database db;
    QString dbErr;
    if (!db.open(dataDirStr + QStringLiteral("/lumina_vault.db"), &dbErr)) {
        QMessageBox::critical(nullptr, QStringLiteral("Error de datos"),
                              QStringLiteral("No se pudo abrir la base de datos:\n%1").arg(dbErr));
        return 1;
    }
    db.setSetting(QStringLiteral("data_dir"), dataDirStr);
    seedDatabase(&db);

    // ---- Motor multimedia (LibVLC dinamica; degrada con elegancia) ----
    MediaEngine media;
    QString vlcErr;
    if (!media.initialize(&vlcErr)) {
        qWarning() << "[Media]" << vlcErr;
    }

    // ---- Servidor web / triggers ----
    WebServer web;
    Triggers triggers;

    // ---- Contexto compartido ----
    AppContext ctx;
    ctx.db = &db;
    ctx.media = &media;
    ctx.web = &web;
    ctx.triggers = &triggers;

    MainWindow w(&ctx);
    w.showMaximized();

    const int rc = app.exec();

    qInfo() << "LuminaPresentation Suite finalizada.";
    return rc;
}
