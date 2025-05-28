// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace OllamaBlazorChat.Models
{
    public class ChatRequest
    {
        public string model { get; set; } = "";
        public List<Message> messages { get; set; } = new List<Message>();
    }
}
