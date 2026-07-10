using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using RagBook.Modules.Documents.Features.UploadDocument;

namespace RagBook.Api.IntegrationTests.Documents;

/// <summary>Drives <c>POST /api/documents</c> (multipart) for a chosen session.</summary>
internal sealed class DocumentApiClient(RagBookApiFactory factory, Guid sessionId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private HttpClient CreateClient()
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = false,
        });
        client.DefaultRequestHeaders.Add("Cookie", $"ragbook_session={sessionId}");

        return client;
    }

    public async Task<(HttpStatusCode Status, DocumentResponse? Document, string? Code)> UploadAsync(
        byte[] content,
        string fileName,
        string contentType,
        Guid? folderId)
    {
        using HttpClient client = CreateClient();
        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(fileContent, "file", fileName);
        if (folderId is Guid id)
        {
            form.Add(new StringContent(id.ToString()), "folderId");
        }

        HttpResponseMessage response = await client.PostAsync("/api/documents", form);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var document = await response.Content.ReadFromJsonAsync<DocumentResponse>(JsonOptions);

            return (response.StatusCode, document, null);
        }

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? code = problem.RootElement.TryGetProperty("code", out JsonElement value) ? value.GetString() : null;

        return (response.StatusCode, null, code);
    }
}
