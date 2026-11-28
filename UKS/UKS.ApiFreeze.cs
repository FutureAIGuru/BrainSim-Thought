/*
 * Brain Simulator Thought
 *
 * Copyright (c) 2026 Charles Simon
 *
 * This file is part of Brain Simulator Thought and is licensed under
 * the MIT License. You may use, copy, modify, merge, publish, distribute,
 * sublicense, and/or sell copies of this software under the terms of
 * the MIT License.
 *
 * See the LICENSE file in the project root for full license information.
 */

using System.Diagnostics.CodeAnalysis;

// The public API of the core is frozen: PublicAPI.Shipped.txt lists it, and adding or removing a
// public member fails the build (RS0016, RS0017) until that file, or PublicAPI.Unshipped.txt, is
// updated on purpose. Agents and helpers are built on the public API but are not part of the frozen
// core (API.md), so their namespaces are left out of the list.
[assembly: SuppressMessage("ApiDesign", "RS0016", Scope = "namespaceanddescendants", Target = "~N:UKS.Agents",
    Justification = "Agents are not part of the frozen core API.")]
[assembly: SuppressMessage("ApiDesign", "RS0016", Scope = "namespaceanddescendants", Target = "~N:UKS.Helpers",
    Justification = "Helpers are not part of the frozen core API.")]
