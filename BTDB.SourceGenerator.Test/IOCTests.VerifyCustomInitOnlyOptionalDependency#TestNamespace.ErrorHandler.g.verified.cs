﻿//HintName: TestNamespace.ErrorHandler.g.cs
// <auto-generated/>
#pragma warning disable 612,618
using System;
using System.Runtime.CompilerServices;

namespace TestNamespace;

static file class ErrorHandlerRegistration
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_Logger")]
    extern static void MethodSetterLogger(global::TestNamespace.ErrorHandler @this, global::TestNamespace.ILogger value);
    [ModuleInitializer]
    internal static unsafe void Register4BTDB()
    {
        global::BTDB.IOC.IContainer.RegisterFactory(typeof(global::TestNamespace.ErrorHandler), (container, ctx) =>
        {
            var f0 = container.CreateFactory(ctx, typeof(global::TestNamespace.ILogger), "Logger");
            return (container2, ctx2) =>
            {
                var res = new global::TestNamespace.ErrorHandler();
                if (f0 != null) MethodSetterLogger(res, Unsafe.As<global::TestNamespace.ILogger>(f0(container2, ctx2)));
                return res;
            };
        });
    }
}
