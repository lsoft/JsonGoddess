using System;
using System.Globalization;
using System.Runtime.CompilerServices;

#if !NET5_0_OR_GREATER

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// <c>[ModuleInitializer]</c> появился в .NET 5; на net472 компилятор
    /// требует этот тип, но довольствуется любым с таким именем.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute
    {
    }
}

#endif

namespace JsonGoddess.Tests
{
    /// <summary>
    /// Культура, под которой идёт весь прогон, - из переменной окружения
    /// <c>JSONGODDESS_TEST_CULTURE</c>.
    ///
    /// <para>
    /// Заведено по случаю, а не впрок. Отказ, разошедшийся с эталоном
    /// разделителем списка, был верен под ru-RU и неверен под en-US, и прогон
    /// на машине разработчика показать этого не мог: он идёт под культурой
    /// этой машины. <see cref="Stj.CultureFixture"/> закрывает места, про
    /// которые уже известно, что они культурно-зависимы; эта переменная
    /// закрывает остальные - те, про которые ещё не известно.
    /// </para>
    ///
    /// <code>
    /// JSONGODDESS_TEST_CULTURE=tr-TR dotnet test
    /// </code>
    ///
    /// <para>
    /// Без переменной не делается ничего: прогон идёт под культурой машины,
    /// как и прежде. Значение <c>invariant</c> означает инвариантную культуру.
    /// </para>
    ///
    /// <para>
    /// Подменяются обе культуры и именно <c>DefaultThreadCurrent*</c>: xUnit
    /// раздаёт тесты по потокам пула, и культура, выставленная на потоке
    /// инициализатора, до них бы не доехала.
    /// </para>
    /// </summary>
    internal static class TestCulture
    {
        [ModuleInitializer]
        internal static void Apply()
        {
            var name = Environment.GetEnvironmentVariable("JSONGODDESS_TEST_CULTURE");

            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            var culture = string.Equals(name, "invariant", StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.InvariantCulture
                : new CultureInfo(name!);

            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
    }
}
