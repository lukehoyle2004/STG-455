using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace CapstoneGame.Auth
{
    public struct HttpResponse
    {
        /// <summary>HTTP status code, or 0 if the server could not be reached.</summary>
        public long StatusCode;
        public string Body;
        /// <summary>True when there was no usable answer from the server (offline, DNS failure, timeout).</summary>
        public bool NetworkError;

        public bool IsSuccess => !NetworkError && StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>Sends HTTP requests. Tests swap in a fake so they never touch the network.</summary>
    public interface IHttpTransport
    {
        Task<HttpResponse> Send(string method, string url, string jsonBody, IReadOnlyDictionary<string, string> headers);
    }

    /// <summary>Real transport built on UnityWebRequest (works on Android, iOS, desktop and WebGL).</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        public int TimeoutSeconds { get; set; } = 15;

        public Task<HttpResponse> Send(string method, string url, string jsonBody, IReadOnlyDictionary<string, string> headers)
        {
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };

            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (headers != null)
                foreach (KeyValuePair<string, string> h in headers)
                    request.SetRequestHeader(h.Key, h.Value);

            var done = new TaskCompletionSource<HttpResponse>();
            request.SendWebRequest().completed += _ =>
            {
                var response = new HttpResponse
                {
                    StatusCode = request.responseCode,
                    Body = request.downloadHandler?.text,
                    NetworkError = request.result == UnityWebRequest.Result.ConnectionError || request.responseCode == 0,
                };
                request.Dispose();
                done.SetResult(response);
            };
            return done.Task;
        }
    }
}
