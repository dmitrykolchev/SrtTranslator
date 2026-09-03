// <copyright file="SubtitleItem.cs" company="Dmitry Kolchev">
// Copyright (c) 2026 Dmitry Kolchev. All rights reserved.
// See LICENSE in the project root for license information
// </copyright>

namespace Xobex.SrtTrans;

public class SubtitleItem
{
    public int Index { get; set; }
    public string StartTime { get; set; } = null!;
    public string EndTime { get; set; } = null!;
    public string Text { get; set; } = null!;
}
