// ============================================================================
//  LuminaPresentation Suite - BiblePanel.h
//  Navegacion biblica multiversion (hasta 3 versiones en paralelo),
//  comandos tipados "Jn 3:16" < 2s, busqueda por palabras FTS.
// ============================================================================
#ifndef LUMINA_BIBLEPANEL_H
#define LUMINA_BIBLEPANEL_H

#include "core/AppContext.h"

#include <QWidget>
#include <QComboBox>
#include <QSpinBox>
#include <QLineEdit>
#include <QListWidget>
#include <QPushButton>
#include <QLabel>
#include <QCheckBox>

class BiblePanel : public QWidget
{
    Q_OBJECT
public:
    explicit BiblePanel(AppContext *ctx, QWidget *parent = nullptr);

    // Referencia seleccionada (para añadir al culto)
    QString currentReference() const;

signals:
    // Pide proyectar un rango biblico: versiones seleccionadas + texto
    void requestProjectVerses(const QStringList &versions, int book, int chapter, int from, int to);
    void requestAddVerseToService(const QStringList &versions, int book, int chapter, int from, int to);

private slots:
    void rebuildVersions();
    void loadChapter();
    void onQuickRef();
    void onWordSearch();
    void onProjectClicked();
    void onAddToServiceClicked();

private:
    void buildUi();
    int selectedBook() const;

    AppContext *m_ctx;
    QComboBox *m_book = nullptr;
    QSpinBox *m_chapter = nullptr;
    QSpinBox *m_vFrom = nullptr;
    QSpinBox *m_vTo = nullptr;
    QComboBox *m_v1 = nullptr;
    QComboBox *m_v2 = nullptr;
    QComboBox *m_v3 = nullptr;
    QLineEdit *m_quick = nullptr;
    QLineEdit *m_wordSearch = nullptr;
    QListWidget *m_list = nullptr;
    QLabel *m_status = nullptr;
};

#endif // LUMINA_BIBLEPANEL_H
