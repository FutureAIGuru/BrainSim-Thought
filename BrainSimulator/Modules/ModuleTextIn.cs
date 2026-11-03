/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License.
 */

using System;

namespace BrainSimulator.Modules;

/// <summary>
/// Compatibility alias for saved networks which still contain ModuleTextIn.
/// New networks should use ModuleText; ModuleBase will open ModuleTextDlg for
/// this derived type because the former ModuleTextIn dialog has been retired.
/// </summary>
[Obsolete("Use ModuleText. ModuleTextIn remains only for saved-network compatibility.")]
public class ModuleTextIn : ModuleText
{
}
