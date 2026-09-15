using JsonGoddess.Generator.Model;
using Microsoft.CodeAnalysis;

namespace JsonGoddess.Generator.Binding
{
    /// <summary>
    /// Отображение типа члена в builtin, который умеют sink'и.
    ///
    /// Список намеренно короче, чем §7.1 плана: там перечислено, что библиотека
    /// будет уметь, а здесь - что она умеет <b>сейчас</b>, и источник истины
    /// один - методы <c>IExhauster</c>/<c>IInjector</c>. Добавлять сюда тип,
    /// которого у sink'а нет, значит породить код, который не скомпилируется.
    /// </summary>
    public static class BuiltinTypes
    {
        /// <summary>
        /// <paramref name="type"/> приезжает сюда уже развёрнутым из
        /// <c>Nullable&lt;&gt;</c>: решение о nullability принадлежит
        /// <see cref="ValueBinder"/>, потому что оно одинаково для builtin'а,
        /// субъекта и коллекции.
        /// </summary>
        public static bool TryBind(
            ITypeSymbol type,
            out BuiltinKind kind,
            out bool isReferenceType
            )
        {
            kind = default;
            isReferenceType = false;

            //byte[] - единственный массив, который известен здесь, и известен он
            //не как коллекция, а как одна base64-строка. Проверка стоит раньше
            //разбора массивов в ValueBinder, иначе base64 превратился бы в
            //список чисел - и это был бы другой документ
            if (type is IArrayTypeSymbol { IsSZArray: true } array
                && array.ElementType.SpecialType == SpecialType.System_Byte)
            {
                kind = BuiltinKind.ByteArray;
                isReferenceType = true;
                return true;
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean: kind = BuiltinKind.Boolean; return true;
                case SpecialType.System_SByte: kind = BuiltinKind.SByte; return true;
                case SpecialType.System_Byte: kind = BuiltinKind.Byte; return true;
                case SpecialType.System_Int16: kind = BuiltinKind.Int16; return true;
                case SpecialType.System_UInt16: kind = BuiltinKind.UInt16; return true;
                case SpecialType.System_Int32: kind = BuiltinKind.Int32; return true;
                case SpecialType.System_UInt32: kind = BuiltinKind.UInt32; return true;
                case SpecialType.System_Int64: kind = BuiltinKind.Int64; return true;
                case SpecialType.System_UInt64: kind = BuiltinKind.UInt64; return true;
                case SpecialType.System_Single: kind = BuiltinKind.Single; return true;
                case SpecialType.System_Double: kind = BuiltinKind.Double; return true;
                case SpecialType.System_Decimal: kind = BuiltinKind.Decimal; return true;
                case SpecialType.System_Char: kind = BuiltinKind.Char; return true;
                case SpecialType.System_DateTime: kind = BuiltinKind.DateTime; return true;

                case SpecialType.System_String:
                {
                    kind = BuiltinKind.String;
                    isReferenceType = true;
                    return true;
                }
            }

            //у DateTimeOffset, TimeSpan и Guid нет SpecialType, поэтому они
            //опознаются по полному имени
            switch (type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            {
                case "global::System.DateTimeOffset": kind = BuiltinKind.DateTimeOffset; return true;
                case "global::System.TimeSpan": kind = BuiltinKind.TimeSpan; return true;
                case "global::System.Guid": kind = BuiltinKind.Guid; return true;
            }

            return false;
        }

        /// <summary>
        /// Чем значение приезжает в документе. Решает не тип, а лексика:
        /// <c>Guid</c> - значимый тип, но в JSON он строка, и читается так же,
        /// как <c>string</c>.
        /// </summary>
        public static LexemeKind GetLexeme(BuiltinKind kind)
        {
            switch (kind)
            {
                case BuiltinKind.Boolean:
                    return LexemeKind.Literal;

                case BuiltinKind.Char:
                case BuiltinKind.String:
                case BuiltinKind.DateTime:
                case BuiltinKind.DateTimeOffset:
                case BuiltinKind.TimeSpan:
                case BuiltinKind.Guid:
                case BuiltinKind.ByteArray:
                    return LexemeKind.Text;

                default:
                    return LexemeKind.Number;
            }
        }

        /// <summary>Имя типа, каким оно печатается в <c>out</c>-переменную читателя.</summary>
        public static string GetTypeName(BuiltinKind kind)
        {
            switch (kind)
            {
                case BuiltinKind.Boolean: return "bool";
                case BuiltinKind.SByte: return "sbyte";
                case BuiltinKind.Byte: return "byte";
                case BuiltinKind.Int16: return "short";
                case BuiltinKind.UInt16: return "ushort";
                case BuiltinKind.Int32: return "int";
                case BuiltinKind.UInt32: return "uint";
                case BuiltinKind.Int64: return "long";
                case BuiltinKind.UInt64: return "ulong";
                case BuiltinKind.Single: return "float";
                case BuiltinKind.Double: return "double";
                case BuiltinKind.Decimal: return "decimal";
                case BuiltinKind.Char: return "char";
                case BuiltinKind.String: return "string";
                case BuiltinKind.DateTime: return "global::System.DateTime";
                case BuiltinKind.DateTimeOffset: return "global::System.DateTimeOffset";
                case BuiltinKind.TimeSpan: return "global::System.TimeSpan";
                case BuiltinKind.Guid: return "global::System.Guid";
                case BuiltinKind.ByteArray: return "byte[]";
                default: return "object";
            }
        }
    }
}
