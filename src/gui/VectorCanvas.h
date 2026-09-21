// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  VectorCanvas.h : Lienzo vectorial libre (estilo PowerPoint) sobre
//  QGraphicsScene 1920x1080. Soporta: cajas de texto enriquecido, formas
//  geometricas (rect/redonda/elipse), imagenes PNG/JPG con canal alfa,
//  seleccion, movimiento, redimension, orden Z y guardado como slide
//  personalizada (JSON) o plantilla de tema.
// ============================================================================
#ifndef LUMINA_VECTORCANVAS_H
#define LUMINA_VECTORCANVAS_H

#include <QGraphicsView>
#include <QGraphicsScene>
#include <QGraphicsTextItem>
#include <QGraphicsRectItem>
#include <QGraphicsEllipseItem>
#include <QGraphicsPixmapItem>
#include <QJsonObject>
#include <QJsonArray>
#include <QMouseEvent>
#include <QKeyEvent>
#include <QColorDialog>
#include <QDebug>

#include "core/Renderer.h"

class VectorCanvas : public QGraphicsView
{
    Q_OBJECT
public:
    enum ShapeKind { ShapeRect, ShapeRound, ShapeEllipse };

    explicit VectorCanvas(QWidget *parent = nullptr) : QGraphicsView(parent)
    {
        m_scene = new QGraphicsScene(0, 0, 1920, 1080, this);
        setScene(m_scene);
        setRenderHint(QPainter::Antialiasing, true);
        setRenderHint(QPainter::TextAntialiasing, true);
        setRenderHint(QPainter::SmoothPixmapTransform, true);
        setDragMode(QGraphicsView::RubberBandDrag);
        setTransformationAnchor(QGraphicsView::AnchorUnderMouse);
        fitInView(sceneRect(), Qt::KeepAspectRatio);
    }

    void fit() { fitInView(sceneRect(), Qt::KeepAspectRatio); }

    // ------------------------- Creacion de elementos ------------------------
    QGraphicsTextItem *addTextItem(const QString &text, const QFont &font, const QColor &color)
    {
        QGraphicsTextItem *it = m_scene->addText(text, font);
        it->setDefaultTextColor(color);
        it->setTextWidth(600);
        it->setPos(600, 400);
        it->setFlags(QGraphicsItem::ItemIsMovable | QGraphicsItem::ItemIsSelectable |
                     QGraphicsItem::ItemIsFocusable);
        it->setTextInteractionFlags(Qt::NoTextInteraction);
        it->setData(KindText, true);
        emit canvasChanged();
        return it;
    }

    // Fabrica un item de forma con la geometria correcta (incluye esquinas
    // redondeadas reales, que QGraphicsRectItem no soporta).
    QGraphicsPathItem *makeShapeItem(ShapeKind kind, const QRectF &rect,
                                     const QPen &pen, const QBrush &brush)
    {
        QPainterPath path;
        if (kind == ShapeEllipse)
            path.addEllipse(rect);
        else if (kind == ShapeRound)
            path.addRoundedRect(rect, 24, 24);
        else
            path.addRect(rect);
        QGraphicsPathItem *it = m_scene->addPath(path, pen, brush);
        it->setFlags(QGraphicsItem::ItemIsMovable | QGraphicsItem::ItemIsSelectable);
        it->setData(kind == ShapeEllipse ? KindEllipse : (kind == ShapeRound ? KindRound : KindRect), true);
        return it;
    }

    QGraphicsItem *addShape(ShapeKind kind)
    {
        const QPen pen(QColor(60, 140, 255), 4);
        const QBrush brush(QColor(30, 60, 140, 200));
        QGraphicsItem *it = nullptr;
        switch (kind) {
        case ShapeRect:
        case ShapeRound:
            it = makeShapeItem(kind, QRectF(600, 350, 500, 250), pen, brush);
            break;
        case ShapeEllipse:
            it = makeShapeItem(ShapeEllipse, QRectF(650, 350, 420, 300), pen, brush);
            break;
        default: return nullptr;
        }
        emit canvasChanged();
        return it;
    }

    QGraphicsPixmapItem *addImage(const QString &path)
    {
        const QPixmap pm(path);
        if (pm.isNull()) return nullptr;
        QGraphicsPixmapItem *it = m_scene->addPixmap(pm);
        it->setPos(450, 250);
        it->setFlags(QGraphicsItem::ItemIsMovable | QGraphicsItem::ItemIsSelectable);
        it->setData(KindImage, true);
        it->setData(PathRole, path);
        emit canvasChanged();
        return it;
    }

    void deleteSelected()
    {
        // CORRECCION: removeItem() solo desvincula; hay que liberar la memoria
        // explicitamente o los items se acumulan (leak).
        const QList<QGraphicsItem *> items = m_scene->selectedItems();
        for (QGraphicsItem *it : items) {
            m_scene->removeItem(it);
            delete it;
        }
        emit canvasChanged();
    }

    void raiseSelected() { for (QGraphicsItem *it : m_scene->selectedItems()) it->setZValue(it->zValue() + 10); emit canvasChanged(); }
    void lowerSelected() { for (QGraphicsItem *it : m_scene->selectedItems()) it->setZValue(it->zValue() - 10); emit canvasChanged(); }

    void clearAll() { m_scene->clear(); emit canvasChanged(); }

    void setBackgroundImage(const QString &path, const QColor &fallback)
    {
        m_bgImage = QImage(path);
        m_bgColor = fallback;
        m_scene->setBackgroundBrush(m_bgImage.isNull() ? QBrush(fallback) : QBrush(m_bgImage));
    }

    // ------------------------- Serializacion JSON ---------------------------
    QJsonArray exportItems() const
    {
        QJsonArray arr;
        const QList<QGraphicsItem *> items = m_scene->items(Qt::AscendingOrder);
        for (QGraphicsItem *gi : items) {
            QJsonObject o;
            o["z"] = gi->zValue();
            const QRectF br = gi->boundingRect().translated(gi->pos());
            o["x"] = br.x(); o["y"] = br.y();
            o["w"] = br.width(); o["h"] = br.height();
            if (gi->data(KindText).toBool()) {
                auto *t = static_cast<QGraphicsTextItem *>(gi);
                o["type"] = QStringLiteral("text");
                o["text"] = t->toPlainText();
                o["html"] = QString::fromUtf8(t->toHtml().toUtf8().toBase64());
            } else if (gi->data(KindImage).toBool()) {
                auto *p = static_cast<QGraphicsPixmapItem *>(gi);
                o["type"] = QStringLiteral("image");
                o["path"] = p->data(PathRole).toString();
            } else if (gi->data(KindEllipse).toBool()) {
                o["type"] = QStringLiteral("ellipse");
                o["pen"] = shapePenJson(gi);
                o["brush"] = shapeBrushJson(gi);
            } else if (gi->data(KindRound).toBool()) {
                o["type"] = QStringLiteral("roundRect");
                o["pen"] = shapePenJson(gi);
                o["brush"] = shapeBrushJson(gi);
            } else if (gi->data(KindRect).toBool()) {
                o["type"] = QStringLiteral("rect");
                o["pen"] = shapePenJson(gi);
                o["brush"] = shapeBrushJson(gi);
            } else {
                continue;
            }
            arr.append(o);
        }
        return arr;
    }

    void loadItems(const QJsonArray &arr)
    {
        m_scene->clear();
        for (const QJsonValue &v : arr) {
            const QJsonObject o = v.toObject();
            const QString type = o.value(QStringLiteral("type")).toString();
            const double x = o.value(QStringLiteral("x")).toDouble();
            const double y = o.value(QStringLiteral("y")).toDouble();
            const double z = o.value(QStringLiteral("z")).toDouble();
            if (type == QStringLiteral("text")) {
                auto *t = m_scene->addText(o.value(QStringLiteral("text")).toString());
                const QString html64 = o.value(QStringLiteral("html")).toString();
                if (!html64.isEmpty())
                    t->setHtml(QString::fromUtf8(QByteArray::fromBase64(html64.toUtf8())));
                t->setDefaultTextColor(Qt::white);
                t->setPos(x, y);
                // CORRECCION v1.2.0: el ancho de texto (600) nunca se restauraba
                // — los textos recargados perdían el ajuste de línea original.
                t->setTextWidth(600);
                t->setFlags(QGraphicsItem::ItemIsMovable | QGraphicsItem::ItemIsSelectable |
                            QGraphicsItem::ItemIsFocusable);
                t->setTextInteractionFlags(Qt::NoTextInteraction);
                t->setData(KindText, true);
                t->setZValue(z);
            } else if (type == QStringLiteral("image")) {
                const QPixmap pm(o.value(QStringLiteral("path")).toString());
                if (pm.isNull()) continue;
                auto *p = m_scene->addPixmap(pm);
                p->setPos(x, y);
                p->setFlags(QGraphicsItem::ItemIsMovable | QGraphicsItem::ItemIsSelectable);
                p->setData(KindImage, true);
                p->setData(PathRole, o.value(QStringLiteral("path")).toString());
                p->setZValue(z);
            } else if (type == QStringLiteral("ellipse")) {
                auto *gi = makeShapeItem(ShapeEllipse, QRectF(x, y, o.value(QStringLiteral("w")).toDouble(),
                                                   o.value(QStringLiteral("h")).toDouble()),
                              penFromJson(o.value(QStringLiteral("pen")).toObject()),
                              brushFromJson(o.value(QStringLiteral("brush")).toObject()));
                gi->setZValue(z);
            } else {
                const ShapeKind k = (type == QStringLiteral("roundRect")) ? ShapeRound : ShapeRect;
                auto *gi = makeShapeItem(k, QRectF(x, y, o.value(QStringLiteral("w")).toDouble(),
                                        o.value(QStringLiteral("h")).toDouble()),
                              penFromJson(o.value(QStringLiteral("pen")).toObject()),
                              brushFromJson(o.value(QStringLiteral("brush")).toObject()));
                gi->setZValue(z);
            }
        }
        // CORRECCION v1.2.0: "z" se guardaba pero nunca se restauraba — el
        // orden de apilado (Subir/Bajar) se perdía al recargar una slide.
        emit canvasChanged();
    }

    // Renderiza el lienzo a un pixmap a la resolucion pedida
    QPixmap renderTo(const QSize &size) const
    {
        QPixmap pm(size);
        pm.fill(m_bgImage.isNull() ? m_bgColor : Qt::black);
        QPainter p(&pm);
        p.setRenderHint(QPainter::Antialiasing, true);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        if (!m_bgImage.isNull())
            Renderer::drawImageFit(p, m_bgImage, pm.rect(), Qt::KeepAspectRatioByExpanding);
        m_scene->render(&p, QRectF(pm.rect()));
        p.end();
        return pm;
    }

    QGraphicsScene *scenePtr() const { return m_scene; }

signals:
    void canvasChanged();

public slots:
    // Doble clic entra a edicion de texto
    void editSelectedText()
    {
        for (QGraphicsItem *gi : m_scene->selectedItems()) {
            if (gi->data(KindText).toBool()) {
                auto *t = static_cast<QGraphicsTextItem *>(gi);
                t->setTextInteractionFlags(Qt::TextEditorInteraction);
                t->setFocus(Qt::MouseFocusReason);
            }
        }
    }

protected:
    void mouseDoubleClickEvent(QMouseEvent *ev) override
    {
        QGraphicsItem *gi = itemAt(ev->pos());
        if (gi && gi->data(KindText).toBool()) {
            auto *t = static_cast<QGraphicsTextItem *>(gi);
            t->setTextInteractionFlags(Qt::TextEditorInteraction);
            t->setFocus(Qt::MouseFocusReason);
        } else {
            QGraphicsView::mouseDoubleClickEvent(ev);
        }
    }

    void keyPressEvent(QKeyEvent *ev) override
    {
        // Desactiva edicion con Escape; borra con Delete/Supr
        if (ev->key() == Qt::Key_Escape) {
            for (QGraphicsItem *gi : m_scene->selectedItems()) {
                if (gi->data(KindText).toBool())
                    static_cast<QGraphicsTextItem *>(gi)->setTextInteractionFlags(Qt::NoTextInteraction);
            }
            clearFocus();
            return;
        }
        if (ev->key() == Qt::Key_Delete || ev->key() == Qt::Key_Backspace) {
            if (!m_scene->selectedItems().isEmpty() &&
                m_scene->focusItem() == nullptr) {
                deleteSelected();
                return;
            }
        }
        QGraphicsView::keyPressEvent(ev);
    }

    void resizeEvent(QResizeEvent *ev) override
    {
        QGraphicsView::resizeEvent(ev);
        fitInView(sceneRect(), Qt::KeepAspectRatio);
    }

private:
    enum DataRole { KindText = 1001, KindRect, KindRound, KindEllipse, KindImage, PathRole = 1010 };
    static QJsonObject shapePenJson(QGraphicsItem *gi)
    {
        QJsonObject o;
        if (auto *p = dynamic_cast<QGraphicsPathItem *>(gi)) {
            o["color"] = p->pen().color().name(QColor::HexArgb);
            o["width"] = int(p->pen().widthF());
        }
        return o;
    }

    static QJsonObject shapeBrushJson(QGraphicsItem *gi)
    {
        QJsonObject o;
        QColor c(Qt::transparent);
        if (auto *p = dynamic_cast<QGraphicsPathItem *>(gi)) c = p->brush().color();
        o["color"] = c.name(QColor::HexArgb);
        return o;
    }

    static QPen penFromJson(const QJsonObject &o)
    {
        return QPen(QColor(o.value(QStringLiteral("color")).toString(QStringLiteral("#3C8CFF"))),
                    o.value(QStringLiteral("width")).toInt(4));
    }

    static QBrush brushFromJson(const QJsonObject &o)
    {
        return QBrush(QColor(o.value(QStringLiteral("color")).toString(QStringLiteral("#1E3C8C"))));
    }

private:
    QGraphicsScene *m_scene = nullptr;
    QImage m_bgImage;
    QColor m_bgColor = QColor(10, 20, 40);
};

#endif // LUMINA_VECTORCANVAS_H
