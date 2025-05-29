// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using OllamaBlazorChat.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

public class ChatService
{
    private readonly HttpClient _http;

    public ChatService(HttpClient http)
    {
        _http = http;
    }

    public async Task<ChatMessage> SendChatAsync(List<ChatMessage> history)
    {
        var request = new ChatRequest
        {
            model = "phi4",
            messages = history.Select(m => new Message
            {
                role = m.Role,
                content = m.Content
            }).ToList()
        };

        try
        {
            // 서버로 POST 요청
            var response = await _http.PostAsJsonAsync("/chat", request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // 실제 JSON 구조에 맞게 수정
            var responseObj = doc.RootElement.GetProperty("response");
            var messages = responseObj.GetProperty("messages");
            var firstMessage = messages[0];
            var contents = firstMessage.GetProperty("contents");
            var firstContent = contents[0];
            var text = firstContent.GetProperty("text").GetString();

            return new ChatMessage
            {
                Role = "assistant",
                Content = text ?? ""
            };
        }
        catch (JsonException ex)
        {
            // JSON 파싱 오류 처리
            throw new InvalidOperationException("Failed to parse chat response", ex);
        }
        catch (HttpRequestException ex)
        {
            // HTTP 요청 오류 처리
            throw new InvalidOperationException("Failed to send chat request", ex);
        }
    }

    //// 스트리밍 응답을 위한 메서드 (선택사항)
    //public async IAsyncEnumerable<string> SendChatStreamAsync(List<ChatMessage> history)
    //{
    //    var request = new ChatRequest
    //    {
    //        model = "phi4",
    //        messages = history.Select(m => new Message
    //        {
    //            role = m.Role,
    //            content = m.Content
    //        }).ToList(),
    //        stream = true // 스트리밍 활성화
    //    };

    //    var response = await _http.PostAsJsonAsync("/chat/stream", request);
    //    response.EnsureSuccessStatusCode();

    //    using var stream = await response.Content.ReadAsStreamAsync();
    //    using var reader = new StreamReader(stream);

    //    string? line;
    //    while ((line = await reader.ReadLineAsync()) != null)
    //    {
    //        if (line.StartsWith("data: ") && line.Length > 6)
    //        {
    //            var jsonData = line.Substring(6);
    //            if (jsonData == "[DONE]") break;

    //            try
    //            {
    //                var doc = JsonDocument.Parse(jsonData);
    //                var responseObj = doc.RootElement.GetProperty("response");
    //                var messages = responseObj.GetProperty("messages");
    //                var firstMessage = messages[0];
    //                var contents = firstMessage.GetProperty("contents");
    //                var firstContent = contents[0];
    //                var text = firstContent.GetProperty("text").GetString();

    //                if (!string.IsNullOrEmpty(text))
    //                {
    //                    yield return text;
    //                }
    //            }
    //            catch (JsonException)
    //            {
    //                // 부분적인 JSON은 무시
    //                continue;
    //            }
    //        }
    //    }
    //}
}