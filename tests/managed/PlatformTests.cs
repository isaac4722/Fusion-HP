// ============================================================================
//  Fusion-HP · tests/managed/PlatformTests.cs — exportadores PPTX/PDF/imágenes
//  y JSON de canciones.
//  v4.0.0: retirados los tests de API HTTP y Triggers — ambas piezas fueron
//  ELIMINADAS del producto por decisión del usuario (sin red, sin OBS, sin
//  control remoto). La suite cubre solo funcionalidad local.
// ============================================================================
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Export;
using Fusion.Studio.Import;
using Fusion.Tests;

namespace Fusion.Tests
{
    static class PlatformTests
    {
        // ------------------------------------------------------------ exportadores
        public static void TestPptxExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "Export" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "uno", "dos", "tres" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.pptx");
            var rep = new PptxExporter().Export(p, outPath, dir);
            TestRunner.Check(rep.Slides == 3, "una diapositiva por línea: " + rep.Slides);
            TestRunner.Check(File.Exists(outPath), "archivo existe");
            // el contenedor es un ZIP OPC válido con [Content_Types].xml
            using (var zip = ZipFile.OpenRead(outPath))
            {
                bool hasTypes = false, hasPresentation = false, hasSlide = false;
                foreach (var e2 in zip.Entries)
                {
                    if (e2.FullName == "[Content_Types].xml") hasTypes = true;
                    if (e2.FullName == "ppt/presentation.xml") hasPresentation = true;
                    if (e2.FullName.StartsWith("ppt/slides/slide", StringComparison.Ordinal)) hasSlide = true;
                }
                TestRunner.Check(hasTypes, "[Content_Types].xml");
                TestRunner.Check(hasPresentation, "presentation.xml");
                TestRunner.Check(hasSlide, "slides/");
                TestRunner.Check(zip.Entries.Count >= 5, "entradas: " + zip.Entries.Count);
            }
            // reimportable por el importador OpenXML
            var p2 = AhpProject.CreateDefault();
            var rep2 = new PptxOpenXmlImporter().Import(outPath, p2);
            TestRunner.Check(rep2.Scenarios >= 1, "reimportado: " + rep2.Scenarios);
        }

        public static void TestPdfExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "PDF" };
            scn.Elements.Add(new Element { Kind = ElementKind.Verse, Reference = "Juan 3:16", Lines = { "texto del verso" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.pdf");
            int pages = new PdfExporter().Export(p, outPath, dir);
            TestRunner.CheckEq(pages, 1, "una página");
            var bytes = File.ReadAllBytes(outPath);
            string head = Encoding.ASCII.GetString(bytes, 0, 5);
            TestRunner.CheckEq(head, "%PDF-", "cabecera PDF");
            string tail = Encoding.ASCII.GetString(bytes, bytes.Length - 8, 8);
            TestRunner.Check(tail.Contains("%%EOF"), "EOF");
            TestRunner.Check(bytes.Length > 800, "tamaño razonable: " + bytes.Length);
        }

        public static void TestImageExporter()
        {
            var p = AhpProject.CreateDefault();
            var scn = new Scenario { Title = "IMG" };
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "una" } });
            scn.Elements.Add(new Element { Kind = ElementKind.Text, Lines = { "dos" } });
            p.Scenarios.Add(scn);
            string dir = TestRunner.TempDir();
            string outPath = Path.Combine(dir, "t.png");
            int n = new ImageExporter().Export(p, outPath, dir);
            TestRunner.CheckEq(n, 2, "dos imágenes");
            string f1 = Path.Combine(dir, "t-001.png");
            TestRunner.Check(File.Exists(f1), "png 1 existe");
            using (var img = System.Drawing.Image.FromFile(f1))
            {
                TestRunner.CheckEq(img.Width, 1920, "ancho 1920 [SPEC §9.3.3]");
                TestRunner.CheckEq(img.Height, 1080, "alto 1080");
            }
        }
    }
}
