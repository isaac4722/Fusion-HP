// ============================================================================
//  LuminaPresentation Suite - BiblePanel.cpp
// ============================================================================
#include "BiblePanel.h"
#include "core/Database.h"
#include "core/BibleRef.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QFormLayout>
#include <QHeaderView>
#include <QMessageBox>

BiblePanel::BiblePanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    rebuildVersions();
}

void BiblePanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    // ---- Comando rapido ----
    auto *quickRow = new QHBoxLayout();
    m_quick = new QLineEdit(this);
    m_quick->setPlaceholderText(
        QStringLiteral("Referencia rápida: escribe “Jn 3:16”, “Salmo 23:1-6”, “1 Co 13:4-7” y presiona Enter"));
    connect(m_quick, &QLineEdit::returnPressed, this, &BiblePanel::onQuickRef);
    quickRow->addWidget(m_quick, 1);
    auto *bGo = new QPushButton(QStringLiteral("Ir"), this);
    connect(bGo, &QPushButton::clicked, this, &BiblePanel::onQuickRef);
    quickRow->addWidget(bGo);
    lay->addLayout(quickRow);

    // ---- Selector de versiones ----
    auto *verRow = new QHBoxLayout();
    verRow->addWidget(new QLabel(QStringLiteral("Principal:"), this));
    m_v1 = new QComboBox(this);
    m_v2 = new QComboBox(this);
    m_v3 = new QComboBox(this);
    m_v2->insertItem(0, QStringLiteral("— ninguna —"));
    m_v3->insertItem(0, QStringLiteral("— ninguna —"));
    verRow->addWidget(m_v1);
    verRow->addWidget(new QLabel(QStringLiteral("Paralela 2:"), this));
    verRow->addWidget(m_v2);
    verRow->addWidget(new QLabel(QStringLiteral("Paralela 3:"), this));
    verRow->addWidget(m_v3);
    verRow->addStretch();
    lay->addLayout(verRow);

    // ---- Navegacion libro/capitulo ----
    auto *navRow = new QHBoxLayout();
    m_book = new QComboBox(this);
    for (const BibleRef::BookInfo &b : BibleRef::books())
        m_book->addItem(QStringLiteral("%1. %2").arg(b.number, 2, 10, QChar('0')).arg(b.name), b.number);
    connect(m_book, qOverload<int>(&QComboBox::currentIndexChanged), this, [this](int) { loadChapter(); });
    m_chapter = new QSpinBox(this);
    m_chapter->setRange(1, 150);
    connect(m_chapter, qOverload<int>(&QSpinBox::valueChanged), this, [this](int) { loadChapter(); });
    m_vFrom = new QSpinBox(this);
    m_vFrom->setPrefix(QStringLiteral("desde v "));
    m_vFrom->setRange(0, 200);
    m_vTo = new QSpinBox(this);
    m_vTo->setPrefix(QStringLiteral("hasta v "));
    m_vTo->setRange(0, 200);
    navRow->addWidget(new QLabel(QStringLiteral("Libro:"), this));
    navRow->addWidget(m_book, 2);
    navRow->addWidget(new QLabel(QStringLiteral("Capítulo:"), this));
    navRow->addWidget(m_chapter);
    navRow->addWidget(m_vFrom);
    navRow->addWidget(m_vTo);
    navRow->addStretch();
    lay->addLayout(navRow);

    // ---- Destacar palabras (v1.1.0 — spec Holyrics: "destacar palabras
    // específicas" en versiculos proyectados) ----
    auto *hlRow = new QHBoxLayout();
    m_highlight = new QLineEdit(this);
    m_highlight->setPlaceholderText(QStringLiteral("Destacar palabras en dorado (separadas por espacio, ej: amor luz gracia)…"));
    m_highlight->setClearButtonEnabled(true);
    hlRow->addWidget(new QLabel(QStringLiteral("✨ Destacar:"), this));
    hlRow->addWidget(m_highlight, 1);
    lay->addLayout(hlRow);

    // ---- Lista de versiculos ----
    m_list = new QListWidget(this);
    m_list->setAlternatingRowColors(true);
    lay->addWidget(m_list, 1);

    // ---- Busqueda por palabras ----
    auto *wordRow = new QHBoxLayout();
    m_wordSearch = new QLineEdit(this);
    m_wordSearch->setPlaceholderText(QStringLiteral("Búsqueda por palabras en toda la Biblia (FTS5)…"));
    m_wordSearch->setClearButtonEnabled(true);
    connect(m_wordSearch, &QLineEdit::returnPressed, this, &BiblePanel::onWordSearch);
    wordRow->addWidget(m_wordSearch, 1);
    auto *bWs = new QPushButton(QStringLiteral("Buscar palabras"), this);
    connect(bWs, &QPushButton::clicked, this, &BiblePanel::onWordSearch);
    wordRow->addWidget(bWs);
    lay->addLayout(wordRow);

    // ---- Botones ----
    auto *btnRow = new QHBoxLayout();
    btnRow->addStretch();
    auto *bAdd = new QPushButton(QStringLiteral("＋ A culto"), this);
    auto *bProj = new QPushButton(QStringLiteral("▶ Proyectar"), this);
    bProj->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:6px 14px;}"));
    connect(bAdd, &QPushButton::clicked, this, &BiblePanel::onAddToServiceClicked);
    connect(bProj, &QPushButton::clicked, this, &BiblePanel::onProjectClicked);
    btnRow->addWidget(bAdd);
    btnRow->addWidget(bProj);
    lay->addLayout(btnRow);

    m_status = new QLabel(QString(), this);
    lay->addWidget(m_status);
}

void BiblePanel::rebuildVersions()
{
    const QStringList vers = m_ctx->db->bibleVersions();
    QComboBox *boxes[] = { m_v1, m_v2, m_v3 };
    for (int b = 0; b < 3; ++b) {
        const QString cur = boxes[b]->currentData().toString();
        boxes[b]->blockSignals(true);
        boxes[b]->clear();
        if (b > 0) boxes[b]->addItem(QStringLiteral("— ninguna —"), QString());
        for (const QString &v : vers) boxes[b]->addItem(v, v);
        const int idx = boxes[b]->findData(cur);
        if (idx >= 0) boxes[b]->setCurrentIndex(idx);
        else if (b == 0 && boxes[b]->count() > 0) boxes[b]->setCurrentIndex(0);
        boxes[b]->blockSignals(false);
    }
}

void BiblePanel::loadChapter()
{
    const int book = selectedBook();
    if (book <= 0) return;
    const QString v1 = m_v1->currentData().toString();
    if (v1.isEmpty()) {
        m_status->setText(QStringLiteral("No hay versiones bíblicas importadas."));
        return;
    }
    const QVector<BibleRef::Verse> verses = m_ctx->db->bibleChapter(v1, book, m_chapter->value());
    m_list->clear();
    if (verses.isEmpty()) {
        m_status->setText(QStringLiteral("Capítulo vacío o versión no importada."));
        return;
    }
    for (const BibleRef::Verse &v : verses) {
        m_list->addItem(QStringLiteral("%1  %2").arg(v.ref.verse, 3).arg(v.text));
    }
    m_status->setText(QStringLiteral("%1 versículos cargados.").arg(verses.size()));
}

int BiblePanel::selectedBook() const
{
    return m_book->currentData().toInt();
}

void BiblePanel::onQuickRef()
{
    const BibleRef::VerseRef r = BibleRef::resolve(m_quick->text());
    if (!r.valid()) {
        QMessageBox::information(this, QStringLiteral("Referencia"),
                                 QStringLiteral("No se pudo interpretar la referencia. Ejemplos válidos:\n"
                                                "Jn 3:16 · Salmo 23 · Génesis 1:1-3 · 1 Co 13:4-7"));
        return;
    }
    m_book->setCurrentIndex(r.book - 1);
    if (r.chapter > 0) m_chapter->setValue(r.chapter);
    // Rango
    const auto rng = BibleRef::rangeOf(m_quick->text());
    if (r.chapter > 0 && rng.first == r.chapter && rng.second.first > 0) {
        m_vFrom->setValue(rng.second.first);
        m_vTo->setValue(rng.second.second);
    } else {
        m_vFrom->setValue(0);
        m_vTo->setValue(0);
    }
    loadChapter();
}

void BiblePanel::onWordSearch()
{
    const QString v1 = m_v1->currentData().toString();
    if (v1.isEmpty()) return;
    const QString term = m_wordSearch->text().simplified();
    if (term.isEmpty()) return;
    const auto results = m_ctx->db->bibleWordSearch(v1, term, 100);
    m_list->clear();
    for (const auto &r : results) {
        const QString ref = BibleRef::formatRef(r.first);
        m_list->addItem(QStringLiteral("[%1]  %2").arg(ref, r.second));
    }
    m_status->setText(QStringLiteral("%1 resultados para “%2”.").arg(results.size()).arg(term));
}

void BiblePanel::onProjectClicked()
{
    const QString v1 = m_v1->currentData().toString();
    if (v1.isEmpty()) return;
    QStringList vers;
    vers << v1;
    // CORRECCION v1.2.0: la versión Paralela 2 no se deduplicaba contra la
    // principal (solo la 3ª se deduplicaba) — elegir la misma versión en
    // Principal y Paralela 2 proyectaba el MISMO texto duplicado en pantalla.
    if (!m_v2->currentData().toString().isEmpty() && m_v2->currentText() != m_v1->currentText())
        vers << m_v2->currentData().toString();
    // FIX v1.1.0: dedupe de la 3ra version contra v1 Y v2 (antes solo v2)
    if (!m_v3->currentData().toString().isEmpty() &&
        m_v3->currentText() != m_v2->currentText() &&
        m_v3->currentText() != m_v1->currentText())
        vers << m_v3->currentData().toString();
    emit requestProjectVerses(vers, selectedBook(), m_chapter->value(),
                              m_vFrom->value(), m_vTo->value(),
                              m_highlight->text().simplified());
}

void BiblePanel::onAddToServiceClicked()
{
    const QString v1 = m_v1->currentData().toString();
    if (v1.isEmpty()) return;
    QStringList vers;
    vers << v1;
    if (!m_v2->currentData().toString().isEmpty()) vers << m_v2->currentData().toString();
    emit requestAddVerseToService(vers, selectedBook(), m_chapter->value(),
                                  m_vFrom->value(), m_vTo->value());
}

QString BiblePanel::currentReference() const
{
    return QStringLiteral("%1 %2:%3-%4").arg(m_book->currentText())
            .arg(m_chapter->value()).arg(m_vFrom->value()).arg(m_vTo->value());
}
