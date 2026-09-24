// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ZipBackup.cs (v5.1.0 «FUNDAMENTO») : respaldo portable de la carpeta data/
//  a un archivo ZIP — implementación PROPIA (net35-safe, cero dependencias):
//    * Deflate: System.IO.Compression.DeflateStream (System.dll 2.0+).
//    * CRC32: implementación tabla (IEEE 802.3, misma que zipfile/Info-ZIP).
//    * Estructura: local file header + data + central directory + EOCD
//      (especificación PKWARE APPNOTE.TXT; abre en Windows Explorer,
//      7-Zip, WinRAR, Google Drive/OneDrive preview).
//  Caso de uso (spec §3.4): "Sincronización en la nube" honesta — la app
//  escribe el respaldo en una carpeta elegida por el usuario (p. ej. la
//  carpeta local de Google Drive / OneDrive si instala su cliente oficial);
//  sin claves OAuth, sin credenciales y sin tocar la nube directamente.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace lumina.core
{
    public static class ZipBackup
    {
        /// <summary>
        /// Crea zipPath con el contenido recursivo de sourceDir (raíz = sourceDir).
        /// Devuelve null si OK; si falla, el mensaje de error (sin excepciones).
        /// </summary>
        public static string CreateFromDirectory(string sourceDir, string zipPath)
        {
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
                return "La carpeta a respaldar no existe: " + sourceDir;
            List<string> files;
            try
            {
                string root = Path.GetFullPath(sourceDir);
                files = new List<string>(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
                files.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex) { return "No se pudo enumerar la carpeta: " + ex.Message; }

            string tmp = zipPath + ".tmp";
            FileStream outFs = null;
            try
            {
                outFs = new FileStream(tmp, FileMode.Create, FileAccess.Write);
                List<CentralEntry> central = new List<CentralEntry>();
                byte[] buffer = new byte[64 * 1024];

                foreach (string file in files)
                {
                    string rel = file.Substring(Path.GetFullPath(sourceDir).Length)
                                       .TrimStart('\\', '/').Replace('\\', '/');
                    CentralEntry ce = WriteEntry(outFs, file, rel, buffer);
                    central.Add(ce);
                }

                // ---- central directory ----
                long cdStart = outFs.Position;
                foreach (CentralEntry e in central)
                {
                    byte[] nameBytes = Encoding.UTF8.GetBytes(e.Name);
                    ZipLow.WriteU32(outFs, 0x02014b50);            // signature
                    ZipLow.WriteU16(outFs, 20);                    // version made by
                    ZipLow.WriteU16(outFs, 20);                    // version needed
                    ZipLow.WriteU16(outFs, 0);                     // flags
                    ZipLow.WriteU16(outFs, e.Method);              // method (coincide con el local header)
                    ZipLow.WriteU16(outFs, e.TimeDos);
                    ZipLow.WriteU16(outFs, e.DateDos);
                    ZipLow.WriteU32(outFs, e.Crc);
                    ZipLow.WriteU32(outFs, e.CompressedSize);
                    ZipLow.WriteU32(outFs, e.UncompressedSize);
                    ZipLow.WriteU16(outFs, (ushort)nameBytes.Length);
                    ZipLow.WriteU16(outFs, 0);                     // extra len
                    ZipLow.WriteU16(outFs, 0);                     // comment len
                    ZipLow.WriteU16(outFs, 0);                     // disk start
                    ZipLow.WriteU16(outFs, 0);                     // internal attrs
                    ZipLow.WriteU32(outFs, 0);                     // external attrs
                    ZipLow.WriteU32(outFs, (uint)e.HeaderOffset);
                    outFs.Write(nameBytes, 0, nameBytes.Length);
                }
                long cdSize = outFs.Position - cdStart;

                // ---- EOCD ----
                ZipLow.WriteU32(outFs, 0x06054b50);
                ZipLow.WriteU16(outFs, 0); ZipLow.WriteU16(outFs, 0);
                ZipLow.WriteU16(outFs, (ushort)central.Count);
                ZipLow.WriteU16(outFs, (ushort)central.Count);
                ZipLow.WriteU32(outFs, (uint)cdSize);
                ZipLow.WriteU32(outFs, (uint)cdStart);
                ZipLow.WriteU16(outFs, 0);
                outFs.Flush();
                outFs.Close(); outFs = null;

                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(tmp, zipPath);
                return null;                                // OK
            }
            catch (Exception ex)
            {
                return "Fallo escribiendo el respaldo: " + ex.Message;
            }
            finally
            {
                if (outFs != null) { try { outFs.Close(); } catch (Exception) { } }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception) { }
            }
        }

        private sealed class CentralEntry
        {
            public string Name;
            public uint Crc;
            public uint CompressedSize;
            public uint UncompressedSize;
            public int HeaderOffset;
            public ushort TimeDos;
            public ushort DateDos;
            public ushort Method;      // 0 = stored (vacíos) · 8 = deflate
        }

        private static CentralEntry WriteEntry(FileStream outFs, string file, string rel,
                                               byte[] buffer)
        {
            CentralEntry e = new CentralEntry();
            e.Name = rel;
            e.HeaderOffset = (int)outFs.Position;

            DateTime mt = File.GetLastWriteTime(file);
            e.TimeDos = (ushort)((mt.Hour << 11) | (mt.Minute << 5) | (mt.Second >> 1));
            e.DateDos = (ushort)(((mt.Year - 1980) << 9) | (mt.Month << 5) | mt.Day);

            // ---- comprimir el cuerpo a memoria (tamaños exactos para el header) ----
            byte[] body;
            using (MemoryStream raw = new MemoryStream())
            {
                using (FileStream inFs = File.OpenRead(file))
                {
                    int n;
                    while ((n = inFs.Read(buffer, 0, buffer.Length)) > 0) raw.Write(buffer, 0, n);
                }
                e.UncompressedSize = (uint)raw.Length;
                byte[] plain = raw.ToArray();
                e.Crc = ZipLow.Crc32(plain, 0, plain.Length);

                if (plain.Length == 0)
                {
                    body = new byte[0];     // entrada vacía: sin deflate (method 0)
                }
                else
                {
                    MemoryStream packed = new MemoryStream();
                    using (DeflateStream ds = new DeflateStream(packed, CompressionMode.Compress, true))
                    {
                        ds.Write(plain, 0, plain.Length);
                    }
                    body = packed.ToArray();
                }
            }
            e.CompressedSize = (uint)body.Length;
            e.Method = (ushort)(body.Length == 0 ? 0 : 8);

            byte[] nameBytes = Encoding.UTF8.GetBytes(rel);
            ZipLow.WriteU32(outFs, 0x04034b50);                    // local file header
            ZipLow.WriteU16(outFs, 20);                            // version needed
            ZipLow.WriteU16(outFs, 0x0800);                        // flags: UTF-8 de nombres
            ZipLow.WriteU16(outFs, e.Method);                      // method (0 vacíos · 8 deflate)
            ZipLow.WriteU16(outFs, e.TimeDos);
            ZipLow.WriteU16(outFs, e.DateDos);
            ZipLow.WriteU32(outFs, e.Crc);
            ZipLow.WriteU32(outFs, e.CompressedSize);
            ZipLow.WriteU32(outFs, e.UncompressedSize);
            ZipLow.WriteU16(outFs, (ushort)nameBytes.Length);
            ZipLow.WriteU16(outFs, 0);                             // extra
            outFs.Write(nameBytes, 0, nameBytes.Length);
            if (body.Length > 0) outFs.Write(body, 0, body.Length);
            return e;
        }
    }
}
