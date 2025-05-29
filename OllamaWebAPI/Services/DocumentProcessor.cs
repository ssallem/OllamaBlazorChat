// ==============================================================================
// Private LLM RAG System with Semantic Kernel (.NET 8)
// ==============================================================================

using Microsoft.Extensions.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Memory;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

// ==============================================================================
// 1. 핵심 모델 정의
// ==============================================================================
public record DocumentMetadata(
    string FileName,
    string FileType,
    DateTime UploadedAt,
    string Department,
    string[] Tags
);

public record RagDocument(
    string Id,
    string Content,
    string Title,
    DocumentMetadata Metadata,
    float[] Embedding
);

public record ChatContext(
    string UserId,
    string SessionId,
    List<ChatMessage> History,
    Dictionary<string, object> Variables
);

// ==============================================================================
// 2. 문서 처리 서비스 (Excel, PDF 지원)
// ==============================================================================
public interface IDocumentProcessor
{
    Task<List<RagDocument>> ProcessDocumentAsync(Stream fileStream, string fileName, DocumentMetadata metadata);
}

public class DocumentProcessor : IDocumentProcessor
{
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<DocumentProcessor> _logger;

    public DocumentProcessor(
        ITextEmbeddingGenerationService embeddingService,
        ILogger<DocumentProcessor> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task<List<RagDocument>> ProcessDocumentAsync(
        Stream fileStream,
        string fileName,
        DocumentMetadata metadata)
    {
        var documents = new List<RagDocument>();
        var fileType = Path.GetExtension(fileName).ToLowerInvariant();

        try
        {
            List<string> textChunks = fileType switch
            {
                ".pdf" => await ExtractPdfTextAsync(fileStream),
                ".xlsx" or ".xls" => await ExtractExcelTextAsync(fileStream),
                ".docx" => await ExtractWordTextAsync(fileStream),
                ".txt" => await ExtractTextFileAsync(fileStream),
                _ => throw new NotSupportedException($"File type {fileType} is not supported")
            };

            // 텍스트 청킹 및 임베딩 생성
            var chunkedTexts = ChunkText(textChunks, maxChunkSize: 1000, overlapSize: 200);

            for (int i = 0; i < chunkedTexts.Count; i++)
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunkedTexts[i]);

                documents.Add(new RagDocument(
                    Id: $"{Path.GetFileNameWithoutExtension(fileName)}_{i}",
                    Content: chunkedTexts[i],
                    Title: $"{fileName} - Chunk {i + 1}",
                    Metadata: metadata,
                    Embedding: embedding.ToArray()
                ));
            }

            _logger.LogInformation($"Processed {documents.Count} chunks from {fileName}");
            return documents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing document {fileName}");
            throw;
        }
    }

    private async Task<List<string>> ExtractPdfTextAsync(Stream stream)
    {
        // iTextSharp 또는 PdfPig 사용
        using var pdfDocument = PdfDocument.Open(stream);
        var texts = new List<string>();

        foreach (var page in pdfDocument.GetPages())
        {
            texts.Add(page.Text);
        }

        return texts;
    }

    private async Task<List<string>> ExtractExcelTextAsync(Stream stream)
    {
        // EPPlus 사용
        using var package = new ExcelPackage(stream);
        var texts = new List<string>();

        foreach (var worksheet in package.Workbook.Worksheets)
        {
            var text = ExtractWorksheetContent(worksheet);
            if (!string.IsNullOrWhiteSpace(text))
            {
                texts.Add(text);
            }
        }

        return texts;
    }

    private string ExtractWorksheetContent(ExcelWorksheet worksheet)
    {
        var content = new StringBuilder();
        var usedRange = worksheet.Dimension;

        if (usedRange == null) return string.Empty;

        for (int row = 1; row <= usedRange.End.Row; row++)
        {
            var rowData = new List<string>();
            for (int col = 1; col <= usedRange.End.Column; col++)
            {
                var cellValue = worksheet.Cells[row, col].Text;
                if (!string.IsNullOrWhiteSpace(cellValue))
                {
                    rowData.Add(cellValue);
                }
            }

            if (rowData.Any())
            {
                content.AppendLine(string.Join(" | ", rowData));
            }
        }

        return content.ToString();
    }

    private List<string> ChunkText(List<string> texts, int maxChunkSize, int overlapSize)
    {
        var chunks = new List<string>();
        var fullText = string.Join("\n\n", texts);

        for (int i = 0; i < fullText.Length; i += maxChunkSize - overlapSize)
        {
            var chunkEnd = Math.Min(i + maxChunkSize, fullText.Length);
            var chunk = fullText.Substring(i, chunkEnd - i);

            if (!string.IsNullOrWhiteSpace(chunk))
            {
                chunks.Add(chunk.Trim());
            }

            if (chunkEnd >= fullText.Length) break;
        }

        return chunks;
    }
}

// ==============================================================================
// 3. 벡터 데이터베이스 서비스 (Qdrant 사용)
// ==============================================================================
public interface IVectorStoreService
{
    Task StoreDocumentsAsync(IEnumerable<RagDocument> documents);
    Task<List<RagDocument>> SearchSimilarAsync(string query, int topK = 5, float threshold = 0.7f);
    Task DeleteDocumentAsync(string documentId);
}

public class QdrantVectorStoreService : IVectorStoreService
{
#pragma warning disable SKEXP0001 // 형식은 평가 목적으로 제공되며, 이후 업데이트에서 변경되거나 제거될 수 있습니다. 계속하려면 이 진단을 표시하지 않습니다.
    private readonly ISemanticTextMemory _memory;
#pragma warning restore SKEXP0001 // 형식은 평가 목적으로 제공되며, 이후 업데이트에서 변경되거나 제거될 수 있습니다. 계속하려면 이 진단을 표시하지 않습니다.
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly ILogger<QdrantVectorStoreService> _logger;
    private const string CollectionName = "company_documents";

#pragma warning disable IDE0290 // 기본 생성자 사용
    public QdrantVectorStoreService(
#pragma warning restore IDE0290 // 기본 생성자 사용
#pragma warning disable SKEXP0001 // 형식은 평가 목적으로 제공되며, 이후 업데이트에서 변경되거나 제거될 수 있습니다. 계속하려면 이 진단을 표시하지 않습니다.
        ISemanticTextMemory memory,
        ITextEmbeddingGenerationService embeddingService,
        ILogger<QdrantVectorStoreService> logger)
    {
        _memory = memory;
        _embeddingService = embeddingService;
        _logger = logger;
    }

    public async Task StoreDocumentsAsync(IEnumerable<RagDocument> documents)
    {
        foreach (var document in documents)
        {
            await _memory.SaveInformationAsync(
                collection: CollectionName,
                text: document.Content,
                id: document.Id,
                description: document.Title,
                additionalMetadata: JsonSerializer.Serialize(document.Metadata)
            );
        }

        _logger.LogInformation($"Stored {documents.Count()} documents in vector store");
    }

    public async Task<List<RagDocument>> SearchSimilarAsync(string query, int topK = 5, float threshold = 0.7f)
    {
        var results = new List<RagDocument>();

        var memories = _memory.SearchAsync(
            collection: CollectionName,
            query: query,
            limit: topK,
            minRelevanceScore: threshold
        );

        await foreach (var memory in memories)
        {
            var metadata = JsonSerializer.Deserialize<DocumentMetadata>(memory.Metadata.AdditionalMetadata);
            var embedding = await _embeddingService.GenerateEmbeddingAsync(memory.Metadata.Text);

            results.Add(new RagDocument(
                Id: memory.Metadata.Id,
                Content: memory.Metadata.Text,
                Title: memory.Metadata.Description,
                Metadata: metadata!,
                Embedding: embedding.ToArray()
            ));
        }

        return results;
    }

    public async Task DeleteDocumentAsync(string documentId)
    {
        await _memory.RemoveAsync(CollectionName, documentId);
        _logger.LogInformation($"Deleted document {documentId} from vector store");
    }
}

// ==============================================================================
// 4. RAG 기반 채팅 서비스
// ==============================================================================
public interface IRagChatService
{
    Task<string> ChatAsync(string userMessage, ChatContext context);
    Task<string> ChatWithDocumentsAsync(string userMessage, List<string> documentIds, ChatContext context);
}

public class RagChatService : IRagChatService
{
    private readonly Kernel _kernel;
    private readonly IVectorStoreService _vectorStore;
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<RagChatService> _logger;

    public RagChatService(
        Kernel kernel,
        IVectorStoreService vectorStore,
        IChatCompletionService chatService,
        ILogger<RagChatService> logger)
    {
        _kernel = kernel;
        _vectorStore = vectorStore;
        _chatService = chatService;
        _logger = logger;
    }

    public async Task<string> ChatAsync(string userMessage, ChatContext context)
    {
        try
        {
            // 1. 관련 문서 검색
            var relevantDocs = await _vectorStore.SearchSimilarAsync(userMessage, topK: 3);

            // 2. 컨텍스트 구성
            var contextText = string.Join("\n\n", relevantDocs.Select(d =>
                $"Document: {d.Title}\nContent: {d.Content}"));

            // 3. 프롬프트 템플릿 적용
            var systemPrompt = CreateSystemPrompt(contextText);

            // 4. 채팅 기록에 시스템 메시지 추가
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);

            // 기존 대화 기록 추가
            foreach (var msg in context.History)
            {
                if (msg.Role == "user")
                    chatHistory.AddUserMessage(msg.Content);
                else
                    chatHistory.AddAssistantMessage(msg.Content);
            }

            // 현재 사용자 메시지 추가
            chatHistory.AddUserMessage(userMessage);

            // 5. LLM 응답 생성
            var response = await _chatService.GetChatMessageContentAsync(chatHistory);

            _logger.LogInformation($"Generated response for user {context.UserId}");
            return response.Content ?? "죄송합니다. 응답을 생성할 수 없습니다.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in RAG chat service");
            return "죄송합니다. 시스템 오류가 발생했습니다.";
        }
    }

    public async Task<string> ChatWithDocumentsAsync(string userMessage, List<string> documentIds, ChatContext context)
    {
        // 특정 문서들만을 대상으로 한 채팅 구현
        // 구현 생략 (위와 유사한 패턴)
        throw new NotImplementedException();
    }

    private string CreateSystemPrompt(string contextText)
    {
        return $"""
        당신은 회사 내부 문서를 기반으로 질문에 답변하는 AI 어시스턴트입니다.
        
        다음 규칙을 준수해 주세요:
        1. 제공된 문서 내용을 바탕으로만 답변하세요.
        2. 확실하지 않은 정보는 추측하지 마세요.
        3. 한국어로 정확하고 친절하게 답변하세요.
        4. 문서에서 관련 정보를 찾을 수 없으면 솔직히 말씀해 주세요.
        5. 가능하면 구체적인 근거와 함께 답변하세요.

        관련 문서 내용:
        {contextText}
        
        위 문서들을 참고하여 사용자의 질문에 답변해 주세요.
        """;
    }
}

// ==============================================================================
// 5. Semantic Kernel 플러그인 정의
// ==============================================================================
public class DocumentManagementPlugin
{
    private readonly IDocumentProcessor _documentProcessor;
    private readonly IVectorStoreService _vectorStore;

    public DocumentManagementPlugin(
        IDocumentProcessor documentProcessor,
        IVectorStoreService vectorStore)
    {
        _documentProcessor = documentProcessor;
        _vectorStore = vectorStore;
    }

    [KernelFunction("upload_document")]
    [Description("사내 문서를 업로드하고 벡터 데이터베이스에 저장합니다.")]
    public async Task<string> UploadDocumentAsync(
        [Description("업로드할 파일의 스트림")] Stream fileStream,
        [Description("파일명")] string fileName,
        [Description("부서명")] string department,
        [Description("문서 태그들")] string[] tags)
    {
        var metadata = new DocumentMetadata(
            FileName: fileName,
            FileType: Path.GetExtension(fileName),
            UploadedAt: DateTime.UtcNow,
            Department: department,
            Tags: tags
        );

        var documents = await _documentProcessor.ProcessDocumentAsync(fileStream, fileName, metadata);
        await _vectorStore.StoreDocumentsAsync(documents);

        return $"문서 '{fileName}'이 성공적으로 업로드되었습니다. {documents.Count}개의 청크로 분할되었습니다.";
    }

    [KernelFunction("search_documents")]
    [Description("특정 키워드로 문서를 검색합니다.")]
    public async Task<string> SearchDocumentsAsync(
        [Description("검색할 키워드")] string query,
        [Description("반환할 최대 결과 수")] int topK = 5)
    {
        var results = await _vectorStore.SearchSimilarAsync(query, topK);

        if (!results.Any())
        {
            return "검색된 문서가 없습니다.";
        }

        var response = new StringBuilder();
        response.AppendLine($"'{query}' 검색 결과 ({results.Count}개):");

        foreach (var doc in results)
        {
            response.AppendLine($"- {doc.Title}");
            response.AppendLine($"  부서: {doc.Metadata.Department}");
            response.AppendLine($"  내용 미리보기: {doc.Content.Substring(0, Math.Min(200, doc.Content.Length))}...");
            response.AppendLine();
        }

        return response.ToString();
    }
}

// ==============================================================================
// 6. 의존성 주입 설정
// ==============================================================================
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPrivateLlmRagSystem(
        this IServiceCollection services,
        string ollamaEndpoint = "http://localhost:11434",
        string qdrantEndpoint = "http://localhost:6333")
    {
        // Semantic Kernel 설정
        services.AddKernel()
            .AddOllamaChatCompletion("phi4", ollamaEndpoint)
            .AddOllamaTextEmbedding("nomic-embed-text", ollamaEndpoint);

        // Vector Store 설정 (Qdrant)
        services.AddSingleton<ISemanticTextMemory>(provider =>
        {
            var embeddingService = provider.GetRequiredService<ITextEmbeddingGenerationService>();
            return new MemoryBuilder()
                .WithQdrantMemoryStore(qdrantEndpoint, 1536)
                .WithTextEmbeddingGeneration(embeddingService)
                .Build();
        });

        // 서비스 등록
        services.AddScoped<IDocumentProcessor, DocumentProcessor>();
        services.AddScoped<IVectorStoreService, QdrantVectorStoreService>();
        services.AddScoped<IRagChatService, RagChatService>();
        services.AddScoped<DocumentManagementPlugin>();

        return services;
    }
}