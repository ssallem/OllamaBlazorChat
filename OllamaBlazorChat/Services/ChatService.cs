// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using OllamaBlazorChat.Models;
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
            model = "deepseek-r1:1.5b",
            messages = history.Select(m => new Message
            {
                role = m.Role,
                content = m.Content
            }).ToList()
        };

        /*
{
  "messages": [
    {
      "role": "assistant",
      "contents": [
        {
          "$type": "text",
          "text": "..."
        }
      ]
    }
  ]
}
         */

        // 서버로 POST 요청
        var response = await _http.PostAsJsonAsync("/chat", request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var messages = doc.RootElement.GetProperty("messages");

        // 예: 첫 번째 메시지에서 text 추출
        var firstMessage = messages[0];  // 메시지 배열 중 첫 번째
        var contents = firstMessage.GetProperty("contents");
        var firstContent = contents[0]; // content 배열 중 첫 번째
        var text = firstContent.GetProperty("text").GetString();

        return new ChatMessage
        {
            Role = "assistant",
            Content = text ?? ""
        };
    }
}
