﻿using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class PropertySet : ICloneable
{
    public PropertyDef Key { get; set; }

    public PropertyDefValue Value { get; set; }

    public object Clone()
    {
        var ret = new PropertySet() {
            Key = this.Key.Clone() as PropertyDef,
            Value = this.Value.Clone() as PropertyDefValue
        };
        return ret;
    }
}

public enum PropertyType
{
    String,
    Integer,
};

[JsonObject(MemberSerialization.OptIn)]
public class PropertyDef : ICloneable
{
    [JsonProperty]
    public string Name { get; set; }

    [JsonProperty]
    public PropertyType Type { get; set; }

    public object Clone()
    {
        var ret = new PropertyDef() { Name = this.Name, Type = this.Type  };
        return ret;
    }
}

public class PropertyDefValue : ICloneable
{
    public string Value { get; set; }

    public object Clone()
    {
        var ret = new PropertyDefValue() { Value = this.Value };
        return ret;
    }

    public override string ToString()
    {
        return Value.ToString();
    }

}