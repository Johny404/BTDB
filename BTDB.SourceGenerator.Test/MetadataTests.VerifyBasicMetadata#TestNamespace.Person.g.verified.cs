﻿//HintName: TestNamespace.Person.g.cs
// <auto-generated/>
using System;
using System.Runtime.CompilerServices;

namespace TestNamespace;

static file class PersonRegistration
{
    [ModuleInitializer]
    internal static void Register4BTDB()
    {
        BTDB.IOC.IContainer.RegisterFactory(typeof(global::TestNamespace.Person), (container, ctx) =>
        {
            return (container2, ctx2) =>
            {
                var res = new global::TestNamespace.Person();
                return res;
            };
        });
    }
}
