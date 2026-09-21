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
#include "core/JsEngine.h"        // v1.5.0: módulos JS / JSLib (spec §3.3)
#include "core/PlanningCenter.h" // v1.5.0: Planning Center Online (spec §3.3)
#include "core/DriveBackup.h"    // v1.5.0: respaldo Google Drive (spec §3.4)
#include "gui/MainWindow.h"

#include <QFileInfo>
#include <QDateTime>
#include <QMessageBox>
#include <cstdio>

static QFile g_logFile;

// v1.5.0 (spec §2.6): log ESTRUCTURADO — timestamp + nivel + categoría
// (módulo emisor) + mensaje. La categoría llega en context.category de
// qInfo()/qWarning() («qt.network», «js», …): permite auditar qué módulo
// escribió cada línea, como exige la especificación.
static void luminaMessageHandler(QtMsgType type, const QMessageLogContext &context, const QString &msg)
{
    const char *level = "INFO ";
    switch (type) {
    case QtWarningMsg: level = "WARN "; break;
    case QtCriticalMsg: level = "CRIT "; break;
    case QtFatalMsg: level = "FATAL"; break;
    case QtInfoMsg: level = "INFO "; break;
    default: level = "DEBUG"; break;
    }
    const QString line = QStringLiteral("[%1] %2 [%3] %4\n")
            .arg(QDateTime::currentDateTime().toString(QStringLiteral("yyyy-MM-dd hh:mm:ss.zzz")),
                 QString::fromLatin1(level),
                 QString::fromLatin1(context.category ? context.category : "app"),
                 msg);
    if (g_logFile.isOpen()) { g_logFile.write(line.toUtf8()); g_logFile.flush(); }
    std::fprintf(stderr, "%s", line.toUtf8().constData());
}

#ifdef Q_OS_WIN
// v1.5.0 (spec §2.6): captura de EXCEPCIONES NO CONTROLADAS (Windows SEH).
// Registra código, dirección y módulo de la falla antes de terminar —
// sustituto ligero del call stack completo (portátil en MinGW-w64, donde
// dbghelp no es fiable); módulo + dirección localizan el fallo con el mapa.
static LONG WINAPI luminaCrashHandler(EXCEPTION_POINTERS *ep)
{
    if (g_logFile.isOpen()) {
        const void *addr = ep->ExceptionRecord->ExceptionAddress;
        HMODULE mod = nullptr;
        wchar_t modName[MAX_PATH] = L"desconocido";
        if (GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                               GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                               static_cast<LPCWSTR>(addr), &mod) && mod)
            GetModuleFileNameW(mod, modName, MAX_PATH);
        const QString line = QStringLiteral(
            "[CRASH] Excepción no controlada: código 0x%1 en 0x%2 (módulo %3)\n")
                .arg(QString::number(ep->ExceptionRecord->ExceptionCode, 16),
                     QString::number(reinterpret_cast<quintptr>(addr), 16),
                     QString::fromWCharArray(modName));
        g_logFile.write(line.toUtf8());
        g_logFile.flush();
    }
    return EXCEPTION_CONTINUE_SEARCH;   // el comportamiento por defecto sigue
}
#endif

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
    QApplication::setApplicationVersion(QStringLiteral("1.6.0"));
    QApplication::setStyle(QStyleFactory::create(QStringLiteral("Fusion")));

    // v1.3.0 GUI "Aurora": hoja de estilos global completa (sidebar, tablas,
    // inputs, scrollbars, docks, chips de estado...) sobre la paleta oscura.
    // La paleta sigue fijando los colores base para los widgets sin QSS.
    QPalette pal;
    pal.setColor(QPalette::Window, QColor(10, 14, 24));
    pal.setColor(QPalette::WindowText, QColor(232, 238, 249));
    pal.setColor(QPalette::Base, QColor(13, 20, 32));
    pal.setColor(QPalette::AlternateBase, QColor(16, 26, 44));
    pal.setColor(QPalette::Text, QColor(232, 238, 249));
    pal.setColor(QPalette::Button, QColor(26, 35, 52));
    pal.setColor(QPalette::ButtonText, QColor(220, 230, 248));
    pal.setColor(QPalette::Highlight, QColor(45, 125, 255));
    pal.setColor(QPalette::HighlightedText, Qt::white);
    pal.setColor(QPalette::ToolTipBase, QColor(22, 33, 58));
    pal.setColor(QPalette::ToolTipText, QColor(220, 230, 248));
    pal.setColor(QPalette::PlaceholderText, QColor(107, 126, 166));
    pal.setColor(QPalette::Disabled, QPalette::Text, QColor(85, 97, 127));
    pal.setColor(QPalette::Disabled, QPalette::ButtonText, QColor(85, 97, 127));
    app.setPalette(pal);

    {
        QFile qss(QStringLiteral(":/styles/aurora.qss"));
        if (qss.open(QIODevice::ReadOnly | QIODevice::Text))
            app.setStyleSheet(QString::fromUtf8(qss.readAll()));
        else
            qWarning() << "[GUI] No se pudo cargar el tema Aurora (aurora.qss)";
    }

    // Log a archivo (se puede desactivar con LUMINA_NO_LOGFILE=1 para depurar).
    // v1.5.0: ROTACIÓN — si lumina.log supera 2 MB se conserva como
    // lumina.old.log (una generación) para que no crezca sin límite.
    const QString dataDirStr = QStandardPaths::writableLocation(QStandardPaths::AppDataLocation);
    QDir().mkpath(dataDirStr);
    if (!qEnvironmentVariableIsSet("LUMINA_NO_LOGFILE")) {
        const QString logPath = dataDirStr + QStringLiteral("/lumina.log");
        QFileInfo logInfo(logPath);
        if (logInfo.exists() && logInfo.size() > 2 * 1024 * 1024) {
            QFile::remove(dataDirStr + QStringLiteral("/lumina.old.log"));
            QFile::rename(logPath, dataDirStr + QStringLiteral("/lumina.old.log"));
        }
        g_logFile.setFileName(logPath);
        if (g_logFile.open(QIODevice::WriteOnly | QIODevice::Append | QIODevice::Text))
            qInstallMessageHandler(luminaMessageHandler);
    }
#ifdef Q_OS_WIN
    // v1.5.0 (spec §2.6): excepciones no controladas -> registro estructurado
    SetUnhandledExceptionFilter(luminaCrashHandler);
#endif

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

    // v1.3.0: copia de seguridad automática semanal del vault (alternativa
    // offline-safe a la sincronización en la nube del spec Holyrics).
    db.autoBackupIfNeeded(dataDirStr + QStringLiteral("/backups"));

    // ---- Motor multimedia (LibVLC dinamica; degrada con elegancia) ----
    MediaEngine media;
    QString vlcErr;
    if (!media.initialize(&vlcErr)) {
        qWarning() << "[Media]" << vlcErr;
    }

    // ---- Servidor web ----
    WebServer web;

    // ---- v1.5.0: módulos JS, Planning Center y Google Drive ----
    // v1.6.0 (B17): JsEngine se declara ANTES que Triggers — la pila se
    // destruye en orden inverso, así Triggers muere primero y su puntero no
    // propietario m_js nunca sobrevive al JsEngine al que apunta.
    JsEngine js;                      // spec §3.3: JSLib (sockets/automatización)

    // ---- Triggers (webhook/OBS/MIDI/Telegram; usa JsEngine) ----
    Triggers triggers;
    PlanningCenter pco;               // spec §3.3: importar planes del culto
    DriveBackup drive;                // spec §3.4: respaldo/sincronización en la nube
    drive.setDatabase(&db);
    triggers.setJsEngine(&js);        // eventos de triggers -> onEvent() de JS
    js.loadModules(dataDirStr);       // <datos>/modules/*.js (cero módulos = no-op)

    // Respaldo automático semanal -> subirlo a Drive si el usuario lo activó
    QObject::connect(&db, &Database::autoBackupCreated, &drive,
                     [&db, &drive](const QString &path) {
        if (drive.hasAccount() &&
            db.setting(QStringLiteral("drive_auto_upload"), QStringLiteral("0")) ==
                QStringLiteral("1")) {
            qInfo() << "[Drive] Subiendo copia automática semanal:" << path;
            drive.backupNow(path);
        }
    });

    // ---- Contexto compartido ----
    AppContext ctx;
    ctx.db = &db;
    ctx.media = &media;
    ctx.web = &web;
    ctx.triggers = &triggers;
    ctx.js = &js;
    ctx.pco = &pco;
    ctx.drive = &drive;

    MainWindow w(&ctx);
    w.showMaximized();

    const int rc = app.exec();

    qInfo() << "LuminaPresentation Suite finalizada.";
    return rc;
}
