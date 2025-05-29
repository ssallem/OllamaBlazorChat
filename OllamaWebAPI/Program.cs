using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using OpenTelemetry.Trace;
using System.ComponentModel;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// ✅ HTTP만 사용, 포트 강제 지정 (예: 5078)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.ListenLocalhost(5078); // HTTP 전용
});

builder.Configuration.AddJsonFile(
    "appsettings.local.json",
    optional: true,
    reloadOnChange: true);

// Swagger 추가
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 캐싱 서비스 추가
builder.Services.AddDistributedMemoryCache();

// OpenTelemetry 설정
var sourceName = Guid.NewGuid().ToString();
var activities = new List<Activity>();

builder.Services.AddSingleton(provider =>
{
    var tracerProvider = OpenTelemetry.Sdk.CreateTracerProviderBuilder()
        .AddSource(sourceName)
        .AddInMemoryExporter(activities)
        .Build();
    return tracerProvider;
});

// 설정에서 엔드포인트만 읽어오기 (ModelId는 동적으로 처리)
var chatEndpoint = new Uri(builder.Configuration["AI:Ollama:Chat:Endpoint"] ?? "http://localhost:11434/");
var embeddingEndpoint = new Uri(builder.Configuration["AI:Ollama:Embedding:Endpoint"] ?? "http://localhost:11434/");

// 기본 모델 ID (요청에서 지정되지 않았을 때 사용)
var defaultChatModelId = builder.Configuration["AI:Ollama:Chat:ModelId"] ?? "llama3.1";
var defaultEmbeddingModelId = builder.Configuration["AI:Ollama:Embedding:ModelId"] ?? "all-minilm";

// Helper 함수들을 서비스로 등록
builder.Services.AddSingleton<Func<string, IChatClient>>(provider =>
{
    var cache = provider.GetRequiredService<IDistributedCache>();
    return (modelId) => new OllamaChatClient(chatEndpoint, modelId);
});

builder.Services.AddSingleton<Func<string, IChatClient>>(provider =>
{
    var cache = provider.GetRequiredService<IDistributedCache>();
    return (modelId) => new OllamaChatClient(chatEndpoint, modelId)
        .AsBuilder()
        .UseDistributedCache(cache)
        .Build();
});

builder.Services.AddSingleton<Func<string, IEmbeddingGenerator<string, Embedding<float>>>>(provider =>
{
    var cache = provider.GetRequiredService<IDistributedCache>();
    return (modelId) => new OllamaEmbeddingGenerator(embeddingEndpoint, modelId);
});

var app = builder.Build();

// Swagger 미들웨어 추가
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ollama API V1");
        c.RoutePrefix = "apis"; // Swagger UI 경로 설정
    });
}

// Helper 함수들
IChatClient CreateChatClient(string? modelId = null, bool useCache = false, bool useTelemetry = false, bool useTools = false)
{
    var model = modelId ?? defaultChatModelId;
    var clientBuilder = new OllamaChatClient(chatEndpoint, model).AsBuilder();

    if (useTools)
        clientBuilder = clientBuilder.UseFunctionInvocation();

    if (useTelemetry)
        clientBuilder = clientBuilder.UseOpenTelemetry(sourceName: sourceName, configure: o => o.EnableSensitiveData = true);

    if (useCache)
    {
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        clientBuilder = clientBuilder.UseDistributedCache(cache);
    }

    return clientBuilder.Build();
}

IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(string? modelId = null, bool useCache = false)
{
    var model = modelId ?? defaultEmbeddingModelId;
    var generator = new OllamaEmbeddingGenerator(embeddingEndpoint, model);

    if (useCache)
    {
        var cache = app.Services.GetRequiredService<IDistributedCache>();
        return generator.AsBuilder().UseDistributedCache(cache).Build();
    }

    return generator;
}

List<ChatMessage> ConvertMessages(List<MessageDto> messages)
{
    return messages.Select(msg => new ChatMessage(
        msg.role.ToLower() switch
        {
            "user" => ChatRole.User,
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        },
        msg.content
    )).ToList();
}

// 기본 채팅 엔드포인트 (모델 ID 동적 지정 가능)
app.MapPost("/chat", async ([FromBody] DynamicChatRequest request) =>
{
    try
    {
        var client = CreateChatClient(request.modelId);
        var recentMessages = request.messages.TakeLast(10).ToList();
        var chatMessages = ConvertMessages(recentMessages);

        var result = await client.GetResponseAsync(chatMessages);
        Console.WriteLine($"✅ Chat Response (Model: {request.modelId ?? defaultChatModelId}): {result}");
        return Results.Ok(new { response = result, model = request.modelId ?? defaultChatModelId });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /chat 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 캐싱된 채팅 엔드포인트
app.MapPost("/chat/cached", async ([FromBody] DynamicChatRequest request) =>
{
    try
    {
        var client = CreateChatClient(request.modelId, useCache: true);
        var recentMessages = request.messages.TakeLast(10).ToList();
        var chatMessages = ConvertMessages(recentMessages);

        var result = await client.GetResponseAsync(chatMessages);
        Console.WriteLine($"✅ Cached Chat Response (Model: {request.modelId ?? defaultChatModelId}): {result}");
        return Results.Ok(new { response = result, model = request.modelId ?? defaultChatModelId, cached = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /chat/cached 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 스트리밍 채팅 엔드포인트
//app.MapPost("/chat/stream", async ([FromBody] DynamicChatRequest request) =>
//{
//    try
//    {
//        var client = CreateChatClient(request.modelId);
//        var recentMessages = request.messages.TakeLast(10).ToList();
//        var chatMessages = ConvertMessages(recentMessages);

//        var response = new List<string>();
//        await foreach (var update in client.GetStreamingResponseAsync(chatMessages))
//        {
//            response.Add(update);
//        }

//        var fullResponse = string.Join("", response);
//        Console.WriteLine($"✅ Streaming Chat Response (Model: {request.modelId ?? defaultChatModelId}): {fullResponse}");
//        return Results.Ok(new { response = fullResponse, model = request.modelId ?? defaultChatModelId, streaming = true });
//    }
//    catch (Exception ex)
//    {
//        Console.WriteLine("❌ /chat/stream 요청 처리 중 오류 발생:");
//        Console.WriteLine(ex.ToString());
//        return Results.Problem("서버 내부 오류가 발생했습니다.");
//    }
//});

// 도구 호출이 포함된 채팅 엔드포인트
app.MapPost("/chat/tools", async ([FromBody] DynamicChatRequest request) =>
{
    try
    {
        var client = CreateChatClient(request.modelId, useTools: true);

        // 날씨 조회 도구 정의
        [Description("Gets the weather")]
        string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
        };

        var recentMessages = request.messages.TakeLast(10).ToList();
        var chatMessages = ConvertMessages(recentMessages);

        var result = await client.GetResponseAsync(chatMessages, chatOptions);
        Console.WriteLine($"✅ Tool Chat Response (Model: {request.modelId ?? defaultChatModelId}): {result}");
        return Results.Ok(new { response = result, model = request.modelId ?? defaultChatModelId, tools_enabled = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /chat/tools 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 모든 기능이 포함된 채팅 엔드포인트
app.MapPost("/chat/full", async ([FromBody] DynamicChatRequest request) =>
{
    try
    {
        var client = CreateChatClient(request.modelId, useCache: true, useTelemetry: true, useTools: true);

        [Description("Gets the weather")]
        string GetWeather() => Random.Shared.NextDouble() > 0.5 ? "It's sunny" : "It's raining";

        var chatOptions = new ChatOptions
        {
            Tools = [AIFunctionFactory.Create(GetWeather)]
        };

        var recentMessages = request.messages.TakeLast(10).ToList();
        var chatMessages = ConvertMessages(recentMessages);

        var result = await client.GetResponseAsync(chatMessages, chatOptions);
        Console.WriteLine($"✅ Full Featured Chat Response (Model: {request.modelId ?? defaultChatModelId}): {result}");
        return Results.Ok(new
        {
            response = result,
            model = request.modelId ?? defaultChatModelId,
            features = new { caching = true, telemetry = true, tools = true }
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /chat/full 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 기본 임베딩 엔드포인트 (모델 ID 동적 지정 가능)
app.MapPost("/embedding", async ([FromBody] DynamicEmbeddingRequest request) =>
{
    try
    {
        var generator = CreateEmbeddingGenerator(request.modelId);
        var result = await generator.GenerateEmbeddingAsync(request.text);
        Console.WriteLine($"✅ Embedding generated (Model: {request.modelId ?? defaultEmbeddingModelId}) for: {request.text}");
        return Results.Ok(new
        {
            embedding = result.Vector.ToArray(),
            text = request.text,
            model = request.modelId ?? defaultEmbeddingModelId
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /embedding 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 캐싱된 임베딩 엔드포인트
app.MapPost("/embedding/cached", async ([FromBody] DynamicEmbeddingRequest request) =>
{
    try
    {
        var generator = CreateEmbeddingGenerator(request.modelId, useCache: true);
        var result = await generator.GenerateEmbeddingAsync(request.text);
        Console.WriteLine($"✅ Cached Embedding generated (Model: {request.modelId ?? defaultEmbeddingModelId}) for: {request.text}");
        return Results.Ok(new
        {
            embedding = result.Vector.ToArray(),
            text = request.text,
            model = request.modelId ?? defaultEmbeddingModelId,
            cached = true
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /embedding/cached 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 벡터만 반환하는 임베딩 엔드포인트
app.MapPost("/embedding/vector", async ([FromBody] DynamicEmbeddingRequest request) =>
{
    try
    {
        var generator = CreateEmbeddingGenerator(request.modelId);
        var result = await generator.GenerateEmbeddingVectorAsync(request.text);
        Console.WriteLine($"✅ Embedding vector generated (Model: {request.modelId ?? defaultEmbeddingModelId}) for: {request.text}");
        return Results.Ok(new
        {
            vector = result.ToArray(),
            text = request.text,
            model = request.modelId ?? defaultEmbeddingModelId
        });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /embedding/vector 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 사용 가능한 모델 목록 조회 (Ollama API 호출)
app.MapGet("/models", async () =>
{
    try
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync($"{chatEndpoint}api/tags");

        if (response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            return Results.Ok(new { models = content, source = "ollama" });
        }
        else
        {
            return Results.Problem("Ollama 서버에서 모델 목록을 가져올 수 없습니다.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /models 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 텔레메트리 정보 조회 엔드포인트
app.MapGet("/telemetry/activities", () =>
{
    try
    {
        var activityData = activities.Select(a => new
        {
            name = a.DisplayName,
            id = a.Id,
            duration = a.Duration.TotalMilliseconds,
            status = a.Status.ToString(),
            tags = a.Tags.ToDictionary(t => t.Key, t => t.Value)
        }).ToList();

        return Results.Ok(new { activities = activityData, count = activities.Count });
    }
    catch (Exception ex)
    {
        Console.WriteLine("❌ /telemetry/activities 요청 처리 중 오류 발생:");
        Console.WriteLine(ex.ToString());
        return Results.Problem("서버 내부 오류가 발생했습니다.");
    }
});

// 헬스 체크 엔드포인트
app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "healthy",
        timestamp = DateTime.UtcNow,
        default_models = new
        {
            chat = defaultChatModelId,
            embedding = defaultEmbeddingModelId
        },
        endpoints = new[]
        {
            "/chat", "/chat/cached", "/chat/stream", "/chat/tools", "/chat/full",
            "/embedding", "/embedding/cached", "/embedding/vector",
            "/models", "/telemetry/activities", "/health"
        }
    });
});

app.Run();

// DTO 클래스들 (모델 ID를 선택적으로 받을 수 있도록 수정)
public record DynamicChatRequest(List<MessageDto> messages, string? modelId = null);
public record DynamicEmbeddingRequest(string text, string? modelId = null);
public record MessageDto(string role, string content);