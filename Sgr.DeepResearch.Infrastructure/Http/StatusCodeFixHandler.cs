using System.Net;
using System.Text;

namespace Sgr.DeepResearch.Infrastructure.Http;

/// <summary>
/// Хендлер для исправления статус-кодов от специфичных провайдеров (например, PolzaAI).
/// Превращает 201 Created в 200 OK, чтобы Semantic Kernel не выбрасывал исключение.
/// </summary>
public class StatusCodeFixHandler : DelegatingHandler
{
    public StatusCodeFixHandler(HttpMessageHandler innerHandler) : base(innerHandler) { }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        // Фикс для провайдеров, которые возвращают 201 Created вместо 200 OK на chat completions
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Берем MediaType или ставим дефолтный json
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "application/json";
            
            var newResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType),
                RequestMessage = response.RequestMessage
            };

            // Копируем заголовки
            foreach (var header in response.Headers)
            {
                newResponse.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return newResponse;
        }

        return response;
    }
}