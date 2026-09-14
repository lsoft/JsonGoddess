using System;
using System.Collections.Generic;

namespace JsonGoddess.Generator.Model
{
    /// <summary>
    /// Файл, который генератор отдаёт компилятору.
    /// </summary>
    public readonly struct GeneratedFile : IEquatable<GeneratedFile>
    {
        public readonly string HintName;
        public readonly string Text;

        public GeneratedFile(string hintName, string text)
        {
            HintName = hintName;
            Text = text;
        }

        public bool Equals(GeneratedFile other) => HintName == other.HintName && Text == other.Text;

        public override bool Equals(object? obj) => obj is GeneratedFile other && Equals(other);

        public override int GetHashCode()
        {
            return unchecked(((HintName?.GetHashCode() ?? 0) * 31) + (Text?.GetHashCode() ?? 0));
        }
    }

    /// <summary>
    /// Последний шаг конвейера (§9 плана). Ни символов, ни узлов, ни
    /// компиляции - только строки, и потому сравнимо по значению.
    ///
    /// Именно здесь, а не на триггере, живёт инкрементальность: связывание
    /// идёт на каждую правку (иначе переименование поля в соседнем файле
    /// осталось бы незамеченным), но если строки совпали, выходной шаг
    /// помечается Cached, и Roslyn переиспользует уже разобранные деревья.
    /// </summary>
    public readonly struct GenerationResult : IEquatable<GenerationResult>
    {
        public static readonly GenerationResult Empty =
            new GenerationResult(EquatableArray<GeneratedFile>.Empty, EquatableArray<DiagnosticInfo>.Empty);

        public readonly EquatableArray<GeneratedFile> Files;
        public readonly EquatableArray<DiagnosticInfo> Diagnostics;

        public GenerationResult(
            EquatableArray<GeneratedFile> files,
            EquatableArray<DiagnosticInfo> diagnostics
            )
        {
            Files = files;
            Diagnostics = diagnostics;
        }

        public GenerationResult(IEnumerable<GeneratedFile> files, IEnumerable<DiagnosticInfo> diagnostics)
            : this(new EquatableArray<GeneratedFile>(files), new EquatableArray<DiagnosticInfo>(diagnostics))
        {
        }

        public bool Equals(GenerationResult other)
        {
            return Files.Equals(other.Files) && Diagnostics.Equals(other.Diagnostics);
        }

        public override bool Equals(object? obj) => obj is GenerationResult other && Equals(other);

        public override int GetHashCode()
        {
            return unchecked((Files.GetHashCode() * 31) + Diagnostics.GetHashCode());
        }
    }
}
