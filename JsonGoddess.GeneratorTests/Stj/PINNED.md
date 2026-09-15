# Вендоренный корпус тестов System.Text.Json

    repository: https://github.com/dotnet/runtime
    tag:        v10.0.12
    commit:     4271d88e0aebf3d04f188f1334c2220d80555ef6
    path:       src/libraries/System.Text.Json/tests/Common/TestClasses
    license:    MIT

Файлы в `corpus/` — **дословные копии**. Ни одной правки: правка превратила бы
чужой набор в свой, а весь смысл переноса в том, что он чужой. Свой набор
всегда описывает то, что автор подумал проверить; этот описывает то, что
ломалось на самом деле — десять лет багрепортов на формат, который держат в
продакшене.

Отсюда и способ употребления: корпус подаётся генератору **текстом**, вместе с
хостом, который печатаем мы. В сборку тестов он не компилируется — половина
его типов существует ровно затем, чтобы генератор от них отказался, а отказ
генератора это ошибка компиляции.

Обновление — `eng/sync-stj-tests.ps1`, и только через него: вендоринг без
зафиксированного коммита через год не даёт ответить, от какой версии эталона
мы отстали.

Версия выбрана под таргет тестов: `v10.0.12` — последняя стабильная 10.x, а на
net10.0 исполняется этот самый корпус.

## Лицензия

`dotnet/runtime` под MIT. Оригинальное уведомление сохранено в заголовке
каждого файла и повторено здесь:

> The MIT License (MIT)
>
> Copyright (c) .NET Foundation and Contributors
>
> All rights reserved.
>
> Permission is hereby granted, free of charge, to any person obtaining a copy
> of this software and associated documentation files (the "Software"), to deal
> in the Software without restriction, including without limitation the rights
> to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
> copies of the Software, and to permit persons to whom the Software is
> furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all
> copies or substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
> IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
> FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
> AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
> LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
> SOFTWARE.
