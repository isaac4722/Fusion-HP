// ============================================================================
//  LuminaPresentation Suite - SongPanel.cpp
// ============================================================================
#include "SongPanel.h"
#include "core/Database.h"
#include "core/Lyrics.h"
#include "core/Chords.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFormLayout>
#include <QHeaderView>
#include <QMessageBox>
#include <QFileDialog>
#include <QTextEdit>
#include <QPlainTextEdit>
#include <QSpinBox>
#include <QCheckBox>
#include <QGroupBox>
#include <QDialog>
#include <QFileInfo>
#include <QRegularExpression>
#include <QFont>

SongPanel::SongPanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    m_searchTimer.setInterval(150);
    m_searchTimer.setSingleShot(true);
    connect(&m_searchTimer, &QTimer::timeout, this, &SongPanel::onSearchChanged);
    reload();
}

void SongPanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    // Busqueda FTS
    auto *searchRow = new QHBoxLayout();
    m_search = new QLineEdit(this);
    m_search->setPlaceholderText(
        QStringLiteral("Buscar por título, autor o extracto de letra… (FTS5 instantáneo)"));
    m_search->setClearButtonEnabled(true);
    connect(m_search, &QLineEdit::textChanged, this, [this]() { m_searchTimer.start(); });
    searchRow->addWidget(m_search, 1);
    m_count = new QLabel(QString(), this);
    searchRow->addWidget(m_count);
    lay->addLayout(searchRow);

    // Tabla de resultados
    m_table = new QTableWidget(this);
    m_table->setColumnCount(4);
    m_table->setHorizontalHeaderLabels({ QStringLiteral("Título"), QStringLiteral("Autor / Grupo"),
                                         QStringLiteral("Tono"), QStringLiteral("BPM") });
    m_table->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    m_table->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    m_table->verticalHeader()->setVisible(false);
    m_table->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_table->setSelectionMode(QAbstractItemView::SingleSelection);
    m_table->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_table->setContextMenuPolicy(Qt::NoContextMenu);
    connect(m_table, &QTableWidget::cellDoubleClicked, this, [this](int, int) { onEdit(); });
    lay->addWidget(m_table, 1);

    // Transposicion (preview de cifras para stage view)
    auto *transRow = new QHBoxLayout();
    transRow->addWidget(new QLabel(QStringLiteral("Transponer cifras (Stage View):"), this));
    auto *transSpin = new QSpinBox(this);
    transSpin->setRange(-11, 11);
    connect(transSpin, qOverload<int>(&QSpinBox::valueChanged), this, &SongPanel::onTransposePreview);
    transRow->addWidget(transSpin);
    transRow->addStretch();
    lay->addLayout(transRow);

    // Botonera
    auto *btnRow = new QHBoxLayout();
    auto mkBtn = [this](const QString &txt, const QString &tip) {
        QPushButton *b = new QPushButton(txt, this);
        b->setToolTip(tip);
        b->setMinimumHeight(32);
        return b;
    };
    auto *bAdd = mkBtn(QStringLiteral("➕ Nueva"), QStringLiteral("Crear canción"));
    auto *bEdit = mkBtn(QStringLiteral("✏ Editar"), QStringLiteral("Editar la canción seleccionada"));
    auto *bDel = mkBtn(QStringLiteral("🗑 Eliminar"), QStringLiteral("Eliminar la canción seleccionada"));
    auto *bDup = mkBtn(QStringLiteral("⧉ Duplicar"), QStringLiteral("Duplicar canción"));
    auto *bImp = mkBtn(QStringLiteral("📂 Importar .txt"), QStringLiteral("Importar canciones desde archivos de texto"));
    auto *bProj = mkBtn(QStringLiteral("▶ Proyectar"), QStringLiteral("Enviar a la salida en vivo (F5)"));
    auto *bQueue = mkBtn(QStringLiteral("＋ A culto"), QStringLiteral("Añadir al culto activo"));
    bProj->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:6px 14px;}"));
    connect(bAdd, &QPushButton::clicked, this, &SongPanel::onAdd);
    connect(bEdit, &QPushButton::clicked, this, &SongPanel::onEdit);
    connect(bDel, &QPushButton::clicked, this, &SongPanel::onDelete);
    connect(bDup, &QPushButton::clicked, this, &SongPanel::onDuplicate);
    connect(bImp, &QPushButton::clicked, this, &SongPanel::onImportText);
    connect(bProj, &QPushButton::clicked, this, [this]() {
        const int id = selectedSongId();
        if (id > 0) emit requestProjectSong(id);
    });
    connect(bQueue, &QPushButton::clicked, this, [this]() {
        const int id = selectedSongId();
        if (id > 0) emit requestAddToService(id);
    });
    btnRow->addWidget(bAdd);
    btnRow->addWidget(bEdit);
    btnRow->addWidget(bDup);
    btnRow->addWidget(bDel);
    btnRow->addWidget(bImp);
    btnRow->addStretch();
    btnRow->addWidget(bQueue);
    btnRow->addWidget(bProj);
    lay->addLayout(btnRow);
}

int SongPanel::selectedSongId() const
{
    const int row = m_table->currentRow();
    if (row < 0) return 0;
    return m_table->item(row, 0)->data(Qt::UserRole).toInt();
}

void SongPanel::reload()
{
    if (!m_search->text().simplified().isEmpty())
        populateTable(m_ctx->db->searchSongs(m_search->text()));
    else
        populateTable(m_ctx->db->allSongs());
}

void SongPanel::onSearchChanged()
{
    reload();
}

void SongPanel::populateTable(const QVector<SongRow> &rows)
{
    m_table->setRowCount(rows.size());
    for (int i = 0; i < rows.size(); ++i) {
        const SongRow &r = rows.at(i);
        auto *c0 = new QTableWidgetItem(r.title);
        c0->setData(Qt::UserRole, r.id);
        m_table->setItem(i, 0, c0);
        m_table->setItem(i, 1, new QTableWidgetItem(r.artist));
        m_table->setItem(i, 2, new QTableWidgetItem(r.key));
        m_table->setItem(i, 3, new QTableWidgetItem(r.bpm > 0 ? QString::number(r.bpm) : QString()));
    }
    m_count->setText(QStringLiteral("%1 canciones").arg(rows.size()));
}

void SongPanel::onAdd()
{
    Song s;
    if (editSongDialog(s, true)) {
        m_ctx->db->addSong(s);
        reload();
    }
}

void SongPanel::onEdit()
{
    const int id = selectedSongId();
    if (id <= 0) return;
    Song s = m_ctx->db->songById(id);
    if (editSongDialog(s, false)) {
        m_ctx->db->updateSong(s);
        reload();
    }
}

void SongPanel::onDelete()
{
    const int id = selectedSongId();
    if (id <= 0) return;
    const Song s = m_ctx->db->songById(id);
    if (QMessageBox::question(this, QStringLiteral("Eliminar canción"),
                              QStringLiteral("¿Eliminar “%1” permanentemente?").arg(s.title)) ==
        QMessageBox::Yes) {
        m_ctx->db->deleteSong(id);
        reload();
    }
}

void SongPanel::onDuplicate()
{
    const int id = selectedSongId();
    if (id <= 0) return;
    Song s = m_ctx->db->songById(id);
    s.id = 0;
    s.title += QStringLiteral(" (copia)");
    m_ctx->db->addSong(s);
    reload();
}

void SongPanel::onImportText()
{
    const QStringList files = QFileDialog::getOpenFileNames(
        this, QStringLiteral("Importar canciones desde texto"),
        QString(), QStringLiteral("Textos (*.txt *.md);;Todos (*)"));
    int imported = 0;
    for (const QString &f : files) {
        QFile file(f);
        if (!file.open(QIODevice::ReadOnly | QIODevice::Text)) continue;
        QString content = QString::fromUtf8(file.readAll());
        file.close();
        Song s;
        s.title = QFileInfo(f).completeBaseName();
        // Primera linea no vacia = titulo si empieza con "@"
        const QStringList lines = content.split(QChar('\n'));
        for (const QString &ln : lines) {
            const QString t = ln.trimmed();
            if (t.isEmpty()) continue;
            if (t.startsWith(QStringLiteral("@titulo "))) s.title = t.mid(8);
            else if (t.startsWith(QStringLiteral("@autor "))) s.artist = t.mid(7);
            else if (t.startsWith(QStringLiteral("@tono "))) s.key = t.mid(6);
            else if (t.startsWith(QStringLiteral("@bpm "))) s.bpm = t.mid(5).toInt();
        }
        content.remove(QRegularExpression(QStringLiteral("^@.*$"), QRegularExpression::MultilineOption));
        s.lyrics = content.trimmed();
        if (!s.lyrics.isEmpty()) { m_ctx->db->addSong(s); imported++; }
    }
    QMessageBox::information(this, QStringLiteral("Importación"),
                             QStringLiteral("Se importaron %1 canciones.").arg(imported));
    reload();
}

void SongPanel::onTransposePreview(int semi)
{
    m_transpose = semi;
    const int id = selectedSongId();
    if (id <= 0 || semi == 0) return;
    const Song s = m_ctx->db->songById(id);
    // Vista previa: transpone la primera linea de acordes encontrada
    const auto sections = Lyrics::parse(s.lyrics);
    for (const auto &sec : sections) {
        for (const auto &ln : sec.lines) {
            if (!ln.chords.isEmpty()) {
                QMessageBox::information(this, QStringLiteral("Vista previa de transposición"),
                                         QStringLiteral("Original:  %1\nTranspuesto: %2")
                                             .arg(ln.chords,
                                                  Chords::transposeLine(ln.chords, semi, true)));
                return;
            }
        }
    }
    QMessageBox::information(this, QStringLiteral("Transposición"),
                             QStringLiteral("Esta canción no tiene cifras/acordes detectados."));
}

bool SongPanel::editSongDialog(Song &song, bool isNew)
{
    QDialog dlg(this);
    dlg.setWindowTitle(isNew ? QStringLiteral("Nueva canción")
                             : QStringLiteral("Editar canción"));
    dlg.resize(720, 640);
    auto *lay = new QVBoxLayout(&dlg);
    auto *form = new QFormLayout();
    auto *edTitle = new QLineEdit(song.title, &dlg);
    auto *edArtist = new QLineEdit(song.artist, &dlg);
    auto *edKey = new QLineEdit(song.key, &dlg);
    auto *edBpm = new QSpinBox(&dlg);
    edBpm->setRange(0, 300);
    edBpm->setValue(song.bpm);
    form->addRow(QStringLiteral("Título:"), edTitle);
    form->addRow(QStringLiteral("Autor / Grupo:"), edArtist);
    form->addRow(QStringLiteral("Tonalidad (ej: Do, Sol, Am):"), edKey);
    form->addRow(QStringLiteral("Tempo (BPM):"), edBpm);
    lay->addLayout(form);

    auto *edLyrics = new QPlainTextEdit(&dlg);
    edLyrics->setPlainText(song.lyrics);
    edLyrics->setFont(QFont(QStringLiteral("Consolas"), 11));
    lay->addWidget(new QLabel(
        QStringLiteral("Etiqueta secciones con [Verso 1], [Coro], [Puente], [Tag]…\n"
                       "Los acordes van en su propia línea ENCIMA de la letra (ej: Do    Sol    Lam).\n"
                       "Ejemplo:\n[Verso 1]\nDo        Sol\nSublime gracia, dulce son"),
        &dlg));
    lay->addWidget(edLyrics, 1);

    auto *btnRow = new QHBoxLayout();
    btnRow->addStretch();
    auto *ok = new QPushButton(QStringLiteral("Guardar"), &dlg);
    auto *cancel = new QPushButton(QStringLiteral("Cancelar"), &dlg);
    connect(ok, &QPushButton::clicked, &dlg, &QDialog::accept);
    connect(cancel, &QPushButton::clicked, &dlg, &QDialog::reject);
    btnRow->addWidget(cancel);
    btnRow->addWidget(ok);
    lay->addLayout(btnRow);

    if (dlg.exec() != QDialog::Accepted) return false;
    song.title = edTitle->text().trimmed();
    if (song.title.isEmpty()) song.title = QStringLiteral("Sin título");
    song.artist = edArtist->text().trimmed();
    song.key = edKey->text().trimmed();
    song.bpm = edBpm->value();
    song.lyrics = edLyrics->toPlainText();
    return true;
}
