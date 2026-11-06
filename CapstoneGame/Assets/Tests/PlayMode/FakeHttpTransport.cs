using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CapstoneGame.Auth;

/// <summary>
/// Stand-in for the network in auth tests: records every request and answers with queued responses,
/// so tests never contact a real Supabase project. Shared by the Edit Mode and Play Mode tests.
/// </summary>
public class FakeHttpTransport : IHttpTransport
{
    public class Request
    {
        public string Method;
        public string Url;
        public string Body;
        public Dictionary<string, string> Headers;
    }

    public readonly List<Request> Requests = new List<Request>();
    private readonly Queue<Func<Task<HttpResponse>>> responses = new Queue<Func<Task<HttpResponse>>>();

    public Request Last => Requests.Count > 0 ? Requests[Requests.Count - 1] : null;

    public void Respond(long status, string body) =>
        responses.Enqueue(() => Task.FromResult(new HttpResponse { StatusCode = status, Body = body }));

    public void RespondNetworkError() =>
        responses.Enqueue(() => Task.FromResult(new HttpResponse { StatusCode = 0, NetworkError = true }));

    /// <summary>The next request waits until the test completes the returned source.</summary>
    public TaskCompletionSource<HttpResponse> HoldNext()
    {
        var pending = new TaskCompletionSource<HttpResponse>();
        responses.Enqueue(() => pending.Task);
        return pending;
    }

    public Task<HttpResponse> Send(string method, string url, string jsonBody, IReadOnlyDictionary<string, string> headers)
    {
        var headerCopy = new Dictionary<string, string>();
        if (headers != null)
            foreach (KeyValuePair<string, string> h in headers) headerCopy[h.Key] = h.Value;

        Requests.Add(new Request { Method = method, Url = url, Body = jsonBody, Headers = headerCopy });

        if (responses.Count == 0) throw new InvalidOperationException($"Unexpected {method} {url}: no fake response queued.");
        return responses.Dequeue()();
    }

    public static string SessionJson(string accessToken = "access-1", string refreshToken = "refresh-1",
        long expiresAt = 0, string email = "pilot@example.com") =>
        "{\"access_token\":\"" + accessToken + "\",\"token_type\":\"bearer\",\"expires_in\":3600," +
        "\"expires_at\":" + expiresAt + ",\"refresh_token\":\"" + refreshToken + "\"," +
        "\"user\":{\"id\":\"11111111-2222-3333-4444-555555555555\",\"email\":\"" + email + "\",\"aud\":\"authenticated\"}}";

    public static AuthSession Session(long expiresAt, string accessToken = "old-access", string refreshToken = "old-refresh") =>
        new AuthSession
        {
            access_token = accessToken,
            refresh_token = refreshToken,
            token_type = "bearer",
            expires_in = 3600,
            expires_at = expiresAt,
            user = new AuthUser { id = "11111111-2222-3333-4444-555555555555", email = "pilot@example.com" },
        };
}
