using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

// 每组独占引擎，组内共享模型并串行调用，组间可以并行识别。
[CollectionDefinition("Native OCR")]
public class NativeOcrCollection : ICollectionFixture<EnemyRecognitionFixture>;

[CollectionDefinition("Shop OCR")]
public class ShopOcrCollection : ICollectionFixture<EnemyRecognitionFixture>, ICollectionFixture<ShopReaderFixture>;

[CollectionDefinition("Avatar OCR")]
public class AvatarOcrCollection : ICollectionFixture<EnemyRecognitionFixture>;

[CollectionDefinition("Ban OCR")]
public class BanOcrCollection : ICollectionFixture<EnemyRecognitionFixture>;

internal static class NativeOcrEngines
{
    private static readonly object Initialization = new();
    private static bool _initialized;

    public static MaaOcrEngine Create()
    {
        // Maa 原生解析器首次使用会写进程级字典；先串行初始化和预热一次，
        // 再让各集合的独立引擎并发工作，不并发操作同一个 MaaTasker。
        lock (Initialization)
        {
            var engine = new MaaOcrEngine(Path.Combine(AppContext.BaseDirectory, "data"));
            if (!_initialized)
            {
                engine.WarmUpAsync().GetAwaiter().GetResult();
                _initialized = true;
            }
            return engine;
        }
    }
}
