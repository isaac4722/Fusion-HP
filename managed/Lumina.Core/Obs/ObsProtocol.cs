// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ObsProtocol.cs : capa de PROTOCOLO obs-websocket 5.x (requisito spec §3.3:
// "envío de texto y versículos en tiempo real mediante WebSocket").
//  Solo construcción/análisis de mensajes + handshake de autenticación:
//  la capa de socket (ClientWebSocket, .NET 4.8) vive en la UI — esta clase
//  es net35-safe y 100% testeable en el arnés.
//
//  Handshake (protocolo V5, "VP"):
//    S→C Hello     {op:0, d:{obsWebSocketVersion, rpcVersion,
//                          authentication:{challenge,salt}}}
//    C→S Identify  {op:1, d:{rpcVersion, eventSubscriptions,
//                          authentication: base64(sha256(base64(sha256(pw+salt))+challenge))}}
//    S→C Identified{op:2, d:{...}}
//    C→S Request   {op:6, d:{requestType, requestId, requestData}}
//    S→C Response  {op:7, d:{requestType, requestId,
//                          requestStatus:{result,code}, responseData}}
//    S→C Event     {op:5, d:{eventType, eventIntent, eventData}}
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace lumina.core
{
    public static class ObsProtocol
    {
        // Opcodes del protocolo V5
        public const int OpHello = 0;
        public const int OpIdentify = 1;
        public const int OpIdentified = 2;
        public const int OpEvent = 5;
        public const int OpRequest = 6;
        public const int OpRequestResponse = 7;

        /// <summary>Suscripciones: solo eventos de escena/entrada (mínimo necesario).</summary>
        public const int EventSubscriptionsBasic = 0;

        /// <summary>
        /// Autenticación V5: base64(sha256(base64(sha256(password+salt)) + challenge)).
        /// Si hello no trae "authentication", devuelve cadena vacía (sin contraseña).
        /// </summary>
        public static string BuildAuth(string password, string salt, string challenge)
        {
            if (string.IsNullOrEmpty(salt) || string.IsNullOrEmpty(challenge)) return string.Empty;
            string pw = password ?? string.Empty;
            // 1) sha256(pw + salt) → base64
            string secret = Base64(Sha256(pw + salt));
            // 2) sha256(secret + challenge) → base64
            return Base64(Sha256(secret + challenge));
        }

        /// <summary>Identify a partir del Hello JSON recibido (texto crudo).</summary>
        public static string BuildIdentify(string helloJson, string password, int rpcVersion,
                                           int eventSubscriptions)
        {
            Dictionary<string, object> hello = MiniJson.Parse(helloJson);
            Dictionary<string, object> d = MiniJson.GetObject(hello, "d");
            long helloRpc = d != null ? MiniJson.GetInt(d, "rpcVersion", 1) : 1;
            Dictionary<string, object> auth = d != null ? MiniJson.GetObject(d, "authentication") : null;

            Dictionary<string, object> id = new Dictionary<string, object>();
            id["rpcVersion"] = helloRpc > 0 ? helloRpc : rpcVersion;
            if (auth != null)
            {
                string a = BuildAuth(password,
                                     MiniJson.GetString(auth, "salt", string.Empty),
                                     MiniJson.GetString(auth, "challenge", string.Empty));
                if (a.Length > 0) id["authentication"] = a;
            }
            id["eventSubscriptions"] = eventSubscriptions;

            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["op"] = OpIdentify;
            msg["d"] = id;
            return MiniJson.Serialize(msg);
        }

        /// <summary>Request genérico {op:6, d:{requestType, requestId, requestData?}}.</summary>
        public static string BuildRequest(string requestType, string requestId,
                                          Dictionary<string, object> requestData)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["requestType"] = requestType;
            d["requestId"] = requestId;
            if (requestData != null && requestData.Count > 0) d["requestData"] = requestData;
            Dictionary<string, object> msg = new Dictionary<string, object>();
            msg["op"] = OpRequest;
            msg["d"] = d;
            return MiniJson.Serialize(msg);
        }

        /// <summary>Cambia la escena programada (SetCurrentProgramScene).</summary>
        public static string BuildSetScene(string requestId, string sceneName)
        {
            Dictionary<string, object> rd = new Dictionary<string, object>();
            rd["sceneName"] = sceneName;
            return BuildRequest("SetCurrentProgramScene", requestId, rd);
        }

        /// <summary>Escribe texto en una fuente (SetInputSettings — p. ej. título OBS).</summary>
        public static string BuildSetInputText(string requestId, string sourceName, string text)
        {
            Dictionary<string, object> settings = new Dictionary<string, object>();
            settings["text"] = text ?? string.Empty;
            Dictionary<string, object> rd = new Dictionary<string, object>();
            rd["inputName"] = sourceName;
            rd["inputSettings"] = settings;
            return BuildRequest("SetInputSettings", requestId, rd);
        }

        /// <summary>Respuesta parseada de un request (op 7) o null si no lo es.</summary>
        public sealed class ObsResponse
        {
            public string RequestType;
            public string RequestId;
            public bool Result;
            public long Code;
            public Dictionary<string, object> ResponseData;
        }

        public static ObsResponse ParseResponse(string json)
        {
            Dictionary<string, object> m = MiniJson.Parse(json);
            if (MiniJson.GetInt(m, "op", -1) != OpRequestResponse) return null;
            Dictionary<string, object> d = MiniJson.GetObject(m, "d");
            if (d == null) return null;
            ObsResponse r = new ObsResponse();
            r.RequestType = MiniJson.GetString(d, "requestType", string.Empty);
            r.RequestId = MiniJson.GetString(d, "requestId", string.Empty);
            Dictionary<string, object> st = MiniJson.GetObject(d, "requestStatus");
            r.Result = st != null && MiniJson.GetBool(st, "result", false);
            r.Code = st != null ? MiniJson.GetInt(st, "code", 0) : 0;
            r.ResponseData = MiniJson.GetObject(d, "responseData");
            return r;
        }

        /// <summary>Evento del servidor (op 5): (eventType, eventData) o null.</summary>
        public static bool ParseEvent(string json, out string eventType, out Dictionary<string, object> eventData)
        {
            eventType = null; eventData = null;
            Dictionary<string, object> m = MiniJson.Parse(json);
            if (MiniJson.GetInt(m, "op", -1) != OpEvent) return false;
            Dictionary<string, object> d = MiniJson.GetObject(m, "d");
            if (d == null) return false;
            eventType = MiniJson.GetString(d, "eventType", string.Empty);
            eventData = MiniJson.GetObject(d, "eventData");
            return true;
        }

        /// <summary>¿Es el Hello inicial (op 0)?</summary>
        public static bool IsHello(string json)
        {
            Dictionary<string, object> m = MiniJson.Parse(json);
            return MiniJson.GetInt(m, "op", -1) == OpHello;
        }

        /// <summary>¿Es el Identified (op 2, handshake completo)?</summary>
        public static bool IsIdentified(string json)
        {
            Dictionary<string, object> m = MiniJson.Parse(json);
            return MiniJson.GetInt(m, "op", -1) == OpIdentified;
        }

        // ---------------------------------------------------------------- hash

        private static byte[] Sha256(string s)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(new UTF8Encoding(false).GetBytes(s ?? string.Empty));
            }
        }

        private static string Base64(byte[] data)
        {
            return Convert.ToBase64String(data);
        }
    }
}
