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
#include <QComboBox>
#include <QLineEdit>
#include <QBrush>
#include <QSet>
#include <QCompleter>

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

    // Fila 1: filtro por etiqueta (tags semanticos v1.0.3)
    auto *tagRow = new QHBoxLayout();
    tagRow->addWidget(new QLabel(QStringLiteral("🏷 Etiqueta:"), this));
    m_tagFilter = new QComboBox(this);
    m_tagFilter->setMinimumWidth(220);
    m_tagFilter->setToolTip(QStringLiteral("Filtrar canciones por etiqueta semántica "
                                            "(las etiquetas se asignan en el editor de cada canción)."));
    connect(m_tagFilter, qOverload<int>(&QComboBox::currentIndexChanged),
            this, &SongPanel::onTagFilterChanged);
    tagRow->addWidget(m_tagFilter, 1);
    tagRow->addSpacing(16);
    lay->addLayout(tagRow);

    // Fila 2: busqueda FTS
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

    // Tabla de resultados (5 columnas: Título, Autor, Tono, BPM, Etiquetas)
    m_table = new QTableWidget(this);
    m_table->setColumnCount(5);
    m_table->setHorizontalHeaderLabels({ QStringLiteral("Título"), QStringLiteral("Autor / Grupo"),
                                         QStringLiteral("Tono"), QStringLiteral("BPM"),
                                         QStringLiteral("Etiquetas") });
    m_table->horizontalHeader()->setSectionResizeMode(0, QHeaderView::Stretch);
    m_table->horizontalHeader()->setSectionResizeMode(1, QHeaderView::Stretch);
    m_table->horizontalHeader()->setSectionResizeMode(2, QHeaderView::ResizeToContents);
    m_table->horizontalHeader()->setSectionResizeMode(3, QHeaderView::ResizeToContents);
    m_table->horizontalHeader()->setSectionResizeMode(4, QHeaderView::Stretch);
    m_table->verticalHeader()->setVisible(false);
    m_table->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_table->setSelectionMode(QAbstractItemView::SingleSelection);
    m_table->setEditTriggers(QAbstractItemView::NoEditTriggers);
    m_table->setContextMenuPolicy(Qt::NoContextMenu);
    connect(m_table, &QTableWidget::cellDoubleClicked, this, [this](int, int) { onEdit(); });
    lay->addWidget(m_table, 1);

    // Transposición (Stage View en vivo — v1.1.0)
    auto *transRow = new QHBoxLayout();
    auto *transLbl = new QLabel(QStringLiteral("Transponer cifras (Stage View en vivo):"), this);
    transLbl->setToolTip(QStringLiteral("Aplica la transposición a las cifras mostradas en el "
                                        "monitor del escenario (los músicos ven los acordes en la nueva tonalidad)."));
    transRow->addWidget(transLbl);
    auto *transSpin = new QSpinBox(this);
    transSpin->setRange(-11, 11);
    transSpin->setSuffix(QStringLiteral(" semitono(s)"));
    // v1.1.0: el valor se persiste y aplica en vivo (antes solo mostraba un mensaje)
    transSpin->setValue(m_ctx->db->setting(QStringLiteral("stage_transpose"), QStringLiteral("0")).toInt());
    connect(transSpin, qOverload<int>(&QSpinBox::valueChanged), this, &SongPanel::onTransposeChanged);
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
    refreshTagFilter();
    const QString tag = m_tagFilter ? m_tagFilter->currentData().toString() : QString();
    const QString term = m_search->text().simplified();

    QVector<SongRow> rows;
    if (!tag.isEmpty()) {
        // Filtro por etiqueta semantica (v1.0.3). Si ademas hay texto FTS,
        // se aplica la interseccion manualmente (caso raro pero correcto).
        QVector<SongRow> byTag = m_ctx->db->searchByTag(tag);
        if (term.isEmpty()) {
            rows = byTag;
        } else {
            const QVector<SongRow> fts = m_ctx->db->searchSongs(term);
            QSet<int> match;
            match.reserve(fts.size());
            for (const SongRow &r : fts) match.insert(r.id);
            for (const SongRow &r : byTag)
                if (match.contains(r.id)) rows.append(r);
        }
    } else if (!term.isEmpty()) {
        rows = m_ctx->db->searchSongs(term);
    } else {
        rows = m_ctx->db->allSongs();
    }
    populateTable(rows);
}

void SongPanel::refreshTagFilter()
{
    if (!m_tagFilter) return;
    // Preservar la seleccion actual del usuario.
    const QString prev = m_tagFilter->currentData().toString();
    QSignalBlocker blk(m_tagFilter);
    m_tagFilter->clear();
    m_tagFilter->addItem(QStringLiteral("(todas)"), QString());
    const auto tags = m_ctx->db->allTags();
    for (const auto &t : tags)
        m_tagFilter->addItem(t.second, t.second);
    // Restaurar seleccion si aun existe
    const int idx = m_tagFilter->findData(prev);
    if (idx >= 0) m_tagFilter->setCurrentIndex(idx);
}

void SongPanel::onTagFilterChanged(int)
{
    reload();
}

void SongPanel::onSearchChanged()
{
    reload();
}

void SongPanel::populateTable(const QVector<SongRow> &rows)
{
    // Cache de etiquetas por cancion (unica consulta en lote por cancion visible).
    m_table->setRowCount(rows.size());
    for (int i = 0; i < rows.size(); ++i) {
        const SongRow &r = rows.at(i);
        auto *c0 = new QTableWidgetItem(r.title);
        c0->setData(Qt::UserRole, r.id);
        m_table->setItem(i, 0, c0);
        m_table->setItem(i, 1, new QTableWidgetItem(r.artist));
        m_table->setItem(i, 2, new QTableWidgetItem(r.key));
        m_table->setItem(i, 3, new QTableWidgetItem(r.bpm > 0 ? QString::number(r.bpm) : QString()));
        // v1.0.3: columna Etiquetas (tags semanticos)
        const QStringList tags = m_ctx->db->songTags(r.id);
        auto *cTags = new QTableWidgetItem(tags.join(QStringLiteral(", ")));
        cTags->setForeground(QBrush(QColor(120, 200, 255)));
        m_table->setItem(i, 4, cTags);
    }
    m_count->setText(QStringLiteral("%1 canciones").arg(rows.size()));
}

void SongPanel::onAdd()
{
    Song s;
    QStringList tags;  // v1.0.3: tags semanticos capturados en el dialogo
    if (editSongDialog(s, tags, true)) {
        const int newId = m_ctx->db->addSong(s);
        if (newId > 0) m_ctx->db->setSongTags(newId, tags);
        reload();
    }
}

void SongPanel::onEdit()
{
    const int id = selectedSongId();
    if (id <= 0) return;
    Song s = m_ctx->db->songById(id);
    QStringList tags = m_ctx->db->songTags(id);  // cargar tags existentes
    if (editSongDialog(s, tags, false)) {
        m_ctx->db->updateSong(s);
        m_ctx->db->setSongTags(id, tags);
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
    const QStringList tags = m_ctx->db->songTags(id);  // duplicar tambien las etiquetas
    s.id = 0;
    s.title += QStringLiteral(" (copia)");
    const int newId = m_ctx->db->addSong(s);
    if (newId > 0) m_ctx->db->setSongTags(newId, tags);
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
        QStringList songTags;   // v1.0.3: importar etiquetas desde @tags a,b,c
        // Primera linea no vacia = titulo si empieza con "@"
        const QStringList lines = content.split(QChar('\n'));
        for (const QString &ln : lines) {
            const QString t = ln.trimmed();
            if (t.isEmpty()) continue;
            if (t.startsWith(QStringLiteral("@titulo "))) s.title = t.mid(8);
            else if (t.startsWith(QStringLiteral("@autor "))) s.artist = t.mid(7);
            else if (t.startsWith(QStringLiteral("@tono "))) s.key = t.mid(6);
            else if (t.startsWith(QStringLiteral("@bpm "))) s.bpm = t.mid(5).toInt();
            else if (t.startsWith(QStringLiteral("@tags "))) {
                const QStringList parts = t.mid(6).split(QChar(','));
                for (const QString &p : parts) {
                    const QString tn = p.trimmed();
                    if (!tn.isEmpty()) songTags << tn;
                }
            }
        }
        content.remove(QRegularExpression(QStringLiteral("^@.*$"), QRegularExpression::MultilineOption));
        s.lyrics = content.trimmed();
        if (!s.lyrics.isEmpty()) {
            const int newId = m_ctx->db->addSong(s);
            if (newId > 0 && !songTags.isEmpty()) m_ctx->db->setSongTags(newId, songTags);
            imported++;
        }
    }
    QMessageBox::information(this, QStringLiteral("Importación"),
                             QStringLiteral("Se importaron %1 canciones.").arg(imported));
    reload();
}

void SongPanel::onTransposeChanged(int semi)
{
    m_transpose = semi;
    // v1.1.0: emite la transposición para que MainWindow la aplique al Stage
    // View EN VIVO y la persista. Además informa el efecto en la barra.
    emit stageTransposeChanged(semi);
}

bool SongPanel::editSongDialog(Song &song, QStringList &tags, bool isNew)
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
    // v1.0.3: editor de etiquetas semanticas (coma-separado).
    // Sugerencia con las etiquetas mas usadas para autocompletar.
    auto *edTags = new QLineEdit(tags.join(QStringLiteral(", ")), &dlg);
    edTags->setPlaceholderText(QStringLiteral("lento, navidad, entrada, ofrenda…"));
    // Completer con las etiquetas existentes (palabra-clave libre)
    QStringList existingTags;
    for (const auto &t : m_ctx->db->allTags()) existingTags << t.second;
    auto *completer = new QCompleter(existingTags, edTags);
    completer->setCaseSensitivity(Qt::CaseInsensitive);
    edTags->setCompleter(completer);
    form->addRow(QStringLiteral("Título:"), edTitle);
    form->addRow(QStringLiteral("Autor / Grupo:"), edArtist);
    form->addRow(QStringLiteral("Tonalidad (ej: Do, Sol, Am):"), edKey);
    form->addRow(QStringLiteral("Tempo (BPM):"), edBpm);
    form->addRow(QStringLiteral("Etiquetas (separadas por coma):"), edTags);
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
    // v1.0.3: capturar etiquetas (separadas por coma)
    tags.clear();
    for (const QString &t : edTags->text().split(QChar(','), Qt::SkipEmptyParts)) {
        const QString tn = t.trimmed();
        if (!tn.isEmpty()) tags << tn;
    }
    return true;
}
