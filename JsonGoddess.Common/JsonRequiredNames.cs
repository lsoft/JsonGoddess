using System.Globalization;

namespace JsonGoddess.Internal
{
    /// <summary>
    /// Разделитель в списке недостающих обязательных имён.
    ///
    /// <para>
    /// Список этот печатает не только наш отказ, но и эталон, и обещание
    /// compat-слоя (§10) - «сообщение то же самое» - обязывает совпасть с ним
    /// дословно. Совпасть константой нельзя: эталон склеивает список
    /// разделителем <b>текущей культуры интерфейса</b>, и на одной и той же
    /// сборке .NET сообщение выглядит по-разному:
    /// </para>
    ///
    /// <code>
    /// ru-RU: ... including: 'Amount'; 'Count'; 'Label'; 'amt'.
    /// en-US: ... including: 'Amount', 'Count', 'Label', 'amt'.
    /// </code>
    ///
    /// <para>
    /// Проверено пробой (<c>scratchpad/ReqMsgProbe</c>), и проверена именно та
    /// культура, которая решает: подмена <c>CurrentCulture</c> при неизменной
    /// <c>CurrentUICulture</c> разделитель не меняет, обратная подмена -
    /// меняет. Поймал расхождение прогон CI: машина разработчика ru-RU,
    /// раннер en-US, и константа <c>"; "</c> была верна ровно на первой.
    /// </para>
    ///
    /// <para>
    /// Значение не кэшируется: культура потока может смениться между
    /// вызовами, а это путь отказа - платить за свойство тут нечем.
    /// </para>
    /// </summary>
    public static class JsonRequiredNames
    {
        public static string Separator
        {
            get
            {
                return CultureInfo.CurrentUICulture.TextInfo.ListSeparator + " ";
            }
        }
    }
}
