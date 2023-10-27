// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Components.Endpoints.FormMapping;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides options for configuring server-side rendering of Razor Components.
/// </summary>
public class RazorComponentOptions
{
    internal readonly FormDataMapperOptions _formMappingOptions = new();

    /// <summary>
    /// Gets or sets the maximum number of elements allowed in a form collection.
    /// </summary>
    public int MaxFormMappingCollectionSize
    {
        get => _formMappingOptions.MaxCollectionSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _formMappingOptions.MaxCollectionSize = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum depth allowed when recursively mapping form data.
    /// </summary>
    public int MaxFormMappingRecursionDepth
    {
        get => _formMappingOptions.MaxRecursionDepth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 1);
            _formMappingOptions.MaxRecursionDepth = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum number of errors allowed when mapping form data.
    /// </summary>
    public int MaxFormMappingErrorCount
    {
        get => _formMappingOptions.MaxErrorCount;
        set
        {
            _formMappingOptions.MaxErrorCount = value;
        }
    }

    /// <summary>
    /// Gets or sets the maximum size of the buffer used to read form data keys.
    /// </summary>
    public int MaxFormMappingKeySize
    {
        get => _formMappingOptions.MaxKeyBufferSize;
        set => _formMappingOptions.MaxKeyBufferSize = value;
    }

    /// <summary>
    /// Gets or sets a value that determines whether the current culture should be used when mapping form data.
    /// </summary>
    public bool FormMappingUseCurrentCulture
    {
        get => _formMappingOptions.UseCurrentCulture;
        set => _formMappingOptions.UseCurrentCulture = value;
    }
}
