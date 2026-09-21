// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SongPanel.h : Gestion de canciones con busqueda FTS instantanea,
//  editor completo (titulo/artista/tono/BPM/letra con etiquetas y acordes),
//  importacion masiva desde texto, transposicion para stage view.
// ============================================================================
#ifndef LUMINA_SONGPANEL_H
#define LUMINA_SONGPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QLineEdit>
#include <QTableWidget>
#include <QPushButton>
#include <QTimer>
#include <QLabel>

class SongPanel : public QWidget
{
    Q_OBJECT
public:
    explicit SongPanel(AppContext *ctx, QWidget *parent = nullptr);

signals:
    void requestProjectSong(int songId);
    void requestAddToService(int songId);

public slots:
    void reload();

private slots:
    void onSearchChanged();
    void onAdd();
    void onEdit();
    void onDelete();
    void onDuplicate();
    void onImportText();
    void onTransposePreview(int);
    bool editSongDialog(Song &song, bool isNew);

private:
    void buildUi();
    void populateTable(const QVector<SongRow> &rows);
    int selectedSongId() const;

    AppContext *m_ctx;
    QLineEdit *m_search = nullptr;
    QTableWidget *m_table = nullptr;
    QLabel *m_count = nullptr;
    QTimer m_searchTimer;
    int m_transpose = 0;
};

#endif // LUMINA_SONGPANEL_H
