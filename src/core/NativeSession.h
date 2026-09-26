// ============================================================================
//  Fusion-HP · NativeSession — lectura de Escenarios empaquetados ahp.v1
//  desde el núcleo C++ [SPEC §4.2 perfil C]. Resuelve un subconjunto real del
//  modelo: Tema → Escenario → Elemento (la plantilla de escenario se aplica
//  cuando está presente). Soporta elementos text (líneas), image y video.
// ============================================================================
#pragma once
#include "Common.h"
#include "SlideState.h"

namespace fusion {

struct NativeItem {
    std::string id;
    std::wstring title;
    std::vector<std::string> tags;
    std::vector<Slide> slides;     // un Elemento → un Slide resuelto
};

class NativeSession {
public:
    // Carga un archivo .ahp (JSON plano; el ZIP empaquetado con media/ lo abre
    // la capa C# — el núcleo lee la variante JSON de archivo único).
    bool Load(const std::wstring& path);
    const std::vector<NativeItem>& Items() const { return items_; }
    std::wstring ProjectName() const { return name_; }
    std::string LastError() const { return error_; }

private:
    std::vector<NativeItem> items_;
    std::wstring name_;
    std::string error_;
    std::wstring baseDir_;   // rutas relativas de media/ se resuelven contra aquí

    void ApplyTheme(Json& style, const Json& theme, const Json& scenario) const;
};

} // namespace fusion
