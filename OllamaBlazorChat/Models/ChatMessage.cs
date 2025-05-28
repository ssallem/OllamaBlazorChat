// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace OllamaBlazorChat.Models;

public class ChatMessage
{
    public string Id { get; set; } = "";
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
