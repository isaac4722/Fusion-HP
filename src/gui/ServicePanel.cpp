// ============================================================================
//  LuminaPresentation Suite - ServicePanel.cpp
// ============================================================================
#include "ServicePanel.h"
#include "core/Database.h"

#include <QVBoxLayout>
#include <QHBoxLayout>
#include <QInputDialog>
#include <QFileDialog>
#include <QMessageBox>
#include <QLabel>

ServicePanel::ServicePanel(AppContext *ctx, QWidget *parent)
    : QWidget(parent), m_ctx(ctx)
{
    buildUi();
    reloadPlaylists();
}

void ServicePanel::buildUi()
{
    auto *lay = new QVBoxLayout(this);
    lay->setContentsMargins(12, 12, 12, 12);

    auto *row = new QHBoxLayout();
    row->addWidget(new QLabel(QStringLiteral("<b>Culto / Servicio:</b>"), this));
    m_services = new QComboBox(this);
    connect(m_services, qOverload<int>(&QComboBox::currentIndexChanged), this, [this](int) {
        const QVariant d = m_services->currentData();
        m_currentServiceId = d.isValid() ? d.toInt() : 0;
        reloadItems();
    });
    row->addWidget(m_services, 1);
    auto *bNew = new QPushButton(QStringLiteral("➕ Nuevo culto"), this);
    auto *bDel = new QPushButton(QStringLiteral("🗑 Eliminar culto"), this);
    connect(bNew, &QPushButton::clicked, this, &ServicePanel::onNewService);
    connect(bDel, &QPushButton::clicked, this, &ServicePanel::onDeleteService);
    row->addWidget(bNew);
    row->addWidget(bDel);
    lay->addLayout(row);

    m_items = new QListWidget(this);
    m_items->setAlternatingRowColors(true);
    lay->addWidget(m_items, 1);

    auto *row2 = new QHBoxLayout();
    auto *bSong = new QPushButton(QStringLiteral("＋ Canción"), this);
    auto *bVerse = new QPushButton(QStringLiteral("＋ Biblia"), this);
    auto *bAviso = new QPushButton(QStringLiteral("＋ Aviso"), this);
    auto *bUp = new QPushButton(QStringLiteral("▲"), this);
    auto *bDown = new QPushButton(QStringLiteral("▼"), this);
    auto *bRemove = new QPushButton(QStringLiteral("Quitar"), this);
    connect(bSong, &QPushButton::clicked, this, &ServicePanel::onAddSong);
    connect(bVerse, &QPushButton::clicked, this, &ServicePanel::onAddVerse);
    connect(bAviso, &QPushButton::clicked, this, &ServicePanel::onAddAviso);
    connect(bUp, &QPushButton::clicked, this, [this]() { onMoveItem(true); });
    connect(bDown, &QPushButton::clicked, this, [this]() { onMoveItem(false); });
    connect(bRemove, &QPushButton::clicked, this, &ServicePanel::onRemoveItem);
    row2->addWidget(bSong);
    row2->addWidget(bVerse);
    row2->addWidget(bAviso);
    row2->addWidget(bUp);
    row2->addWidget(bDown);
    row2->addWidget(bRemove);
    row2->addStretch();
    lay->addLayout(row2);

    auto *row3 = new QHBoxLayout();
    auto *bStart = new QPushButton(QStringLiteral("▶ Iniciar culto (cargar en vivo)"), this);
    bStart->setStyleSheet(QStringLiteral("QPushButton{background:#1E6FD9;color:white;font-weight:bold;padding:8px 16px;}"));
    auto *bCsv = new QPushButton(QStringLiteral("Exportar orden CSV"), this);
    connect(bStart, &QPushButton::clicked, this, [this]() {
        if (m_currentServiceId > 0) emit startService(m_currentServiceId);
    });
    connect(bCsv, &QPushButton::clicked, this, &ServicePanel::onExportCsv);
    row3->addWidget(bStart);
    row3->addWidget(bCsv);
    row3->addStretch();
    lay->addLayout(row3);
}

void ServicePanel::reloadPlaylists(int selectId)
{
    m_services->blockSignals(true);
    m_services->clear();
    const auto list = m_ctx->db->playlists();
    for (const auto &p : list)
        m_services->addItem(p.second, p.first);
    if (selectId > 0) {
        const int idx = m_services->findData(selectId);
        if (idx >= 0) m_services->setCurrentIndex(idx);
    }
    m_services->blockSignals(false);
    m_currentServiceId = m_services->currentData().isValid() ? m_services->currentData().toInt() : 0;
    reloadItems();
}

void ServicePanel::reloadItems()
{
    m_items->clear();
    if (m_currentServiceId <= 0) return;
    static const char *kinds[] = { "🎵", "📖", "📽", "🖼", "🎬", "🎨", "📢" };
    const auto items = m_ctx->db->playlistItems(m_currentServiceId);
    int pos = 1;
    for (const ServiceItem &it : items) {
        const QString icon = it.kind >= 0 && it.kind <= 6 ? QString::fromUtf8(kinds[it.kind]) : QStringLiteral("•");
        auto *i = new QListWidgetItem(QStringLiteral("%1  %2.  %3").arg(icon).arg(pos++).arg(it.label));
        i->setData(Qt::UserRole, it.id);
        m_items->addItem(i);
    }
}

void ServicePanel::onNewService()
{
    bool ok = false;
    const QString name = QInputDialog::getText(this, QStringLiteral("Nuevo culto"),
                                               QStringLiteral("Nombre del culto/servicio:"),
                                               QLineEdit::Normal,
                                               QDate::currentDate().toString(QStringLiteral("ddd dd 'de' MMMM yyyy")),
                                               &ok);
    if (!ok || name.trimmed().isEmpty()) return;
    const int id = m_ctx->db->createPlaylist(name.trimmed());
    reloadPlaylists(id);
}

void ServicePanel::onDeleteService()
{
    if (m_currentServiceId <= 0) return;
    if (QMessageBox::question(this, QStringLiteral("Eliminar culto"),
                              QStringLiteral("¿Eliminar este culto y sus items?")) == QMessageBox::Yes) {
        m_ctx->db->deletePlaylist(m_currentServiceId);
        reloadPlaylists();
    }
}

void ServicePanel::onAddSong()
{
    if (m_currentServiceId <= 0) return;
    const auto songs = m_ctx->db->allSongs();
    QStringList names;
    QVector<int> ids;
    for (const SongRow &s : songs) { names << s.title; ids << s.id; }
    bool ok = false;
    const QString pick = QInputDialog::getItem(this, QStringLiteral("Añadir canción"),
                                               QStringLiteral("Canción:"), names, 0, false, &ok);
    if (!ok) return;
    const int idx = names.indexOf(pick);
    if (idx < 0) return;
    ServiceItem it;
    it.kind = ServiceItem::Song;
    it.refId = ids.at(idx);
    it.label = pick;
    m_ctx->db->addPlaylistItem(m_currentServiceId, it);
    reloadItems();
}

void ServicePanel::onAddVerse()
{
    if (m_currentServiceId <= 0) return;
    bool ok = false;
    const QString ref = QInputDialog::getText(this, QStringLiteral("Añadir Biblia"),
                                              QStringLiteral("Referencia (ej. Jn 3:16):"),
                                              QLineEdit::Normal, QString(), &ok);
    if (!ok || ref.trimmed().isEmpty()) return;
    ServiceItem it;
    it.kind = ServiceItem::Bible;
    it.label = QStringLiteral("Biblia: %1").arg(ref.trimmed());
    it.payload = ref.trimmed();
    m_ctx->db->addPlaylistItem(m_currentServiceId, it);
    reloadItems();
}

void ServicePanel::onAddAviso()
{
    if (m_currentServiceId <= 0) return;
    bool ok = false;
    const QString txt = QInputDialog::getMultiLineText(this, QStringLiteral("Aviso"),
                                                       QStringLiteral("Texto del aviso:"), QString(), &ok);
    if (!ok || txt.trimmed().isEmpty()) return;
    ServiceItem it;
    it.kind = ServiceItem::Aviso;
    it.label = QStringLiteral("Aviso: %1").arg(txt.left(40));
    it.payload = txt;
    m_ctx->db->addPlaylistItem(m_currentServiceId, it);
    reloadItems();
}

void ServicePanel::onRemoveItem()
{
    auto *it = m_items->currentItem();
    if (!it) return;
    m_ctx->db->removePlaylistItem(it->data(Qt::UserRole).toInt());
    reloadItems();
}

void ServicePanel::onMoveItem(bool up)
{
    auto *it = m_items->currentItem();
    if (!it) return;
    m_ctx->db->movePlaylistItem(it->data(Qt::UserRole).toInt(), up);
    reloadItems();
    const int row = m_items->currentRow() + (up ? -1 : 1);
    if (row >= 0 && row < m_items->count()) m_items->setCurrentRow(row);
}

void ServicePanel::onExportCsv()
{
    if (m_currentServiceId <= 0) return;
    const QString out = QFileDialog::getSaveFileName(this, QStringLiteral("Exportar orden del culto"),
                                                     QStringLiteral("culto.csv"), QStringLiteral("CSV (*.csv)"));
    if (out.isEmpty()) return;
    QFile f(out);
    if (!f.open(QIODevice::WriteOnly | QIODevice::Text)) return;
    QTextStream ts(&f);
    ts.setCodec("UTF-8");
    ts << QStringLiteral("orden;item\n");
    int pos = 1;
    for (const ServiceItem &it : m_ctx->db->playlistItems(m_currentServiceId))
        ts << QStringLiteral("%1;%2\n").arg(pos++).arg(it.label);
    f.close();
    QMessageBox::information(this, QStringLiteral("CSV"), QStringLiteral("Orden exportado."));
}
