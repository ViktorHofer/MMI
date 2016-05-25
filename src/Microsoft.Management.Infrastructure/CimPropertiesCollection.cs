﻿/*============================================================================
 * Copyright (C) Microsoft Corporation, All rights reserved. 
 *============================================================================
 */

using System;
using System.Collections.Generic;
using Microsoft.Management.Infrastructure.Generic;
using Microsoft.Management.Infrastructure.Options.Internal;
using NativeObject;

namespace Microsoft.Management.Infrastructure.Internal.Data
{
    internal class CimPropertiesCollection : CimKeyedCollection<CimProperty>
    {
        private readonly SharedInstanceHandle _instanceHandle;
        private readonly CimInstance _instance;

        internal CimPropertiesCollection(SharedInstanceHandle instanceHandle, CimInstance instance)
        {
            this._instanceHandle = instanceHandle;
            this._instance = instance;
        }

        public override void Add(CimProperty newProperty)
        {
            if (newProperty == null)
            {
                throw new ArgumentNullException("newProperty");
            }

            MI_Result result =  this._instanceHandle.Handle.AddElement(
                newProperty.Name,
                CimInstance.ConvertToNativeLayer(newProperty.Value),
                newProperty.CimType.ToMiType(),
                newProperty.Flags.ToMiFlags());
            CimException.ThrowIfMiResultFailure(result);
        }

        public override int Count
        {
            get
            {
                uint count;
                MI_Result result = this._instanceHandle.Handle.GetElementCount(out count);
                CimException.ThrowIfMiResultFailure(result);
                return (int)count;
            }
        }

        public override CimProperty this[string propertyName]
        {
            get
            {
                if (string.IsNullOrWhiteSpace(propertyName))
                {
                    throw new ArgumentNullException("propertyName");
                }

		MI_Value value;
		MI_Type type;
		MI_Flags flags;
		UInt32 index;
                MI_Result result = this._instanceHandle.Handle.GetElement(propertyName,
									  out value,
									  out type,
									  out flags,
									  out index);
                switch (result)
                {
                    case MI_Result.MI_RESULT_NO_SUCH_PROPERTY:
                        return null;

                    default:
                        CimException.ThrowIfMiResultFailure(result);
                        return new CimPropertyOfInstance(this._instanceHandle, this._instance, (int)index);
                }
            }
        }

        public override IEnumerator<CimProperty> GetEnumerator()
        {
            int count = this.Count;
            for (int i = 0; i < count; i++)
            {
                yield return new CimPropertyOfInstance(this._instanceHandle, this._instance, i);
            }
        }
    }
}