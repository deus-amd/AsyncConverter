﻿using System.Collections;
using System.Threading.Tasks;

namespace AsyncConverter.Tests.Test.Data.FixReturnValueToTaskTests
{
    public class Class
    {
        public Task Test()
        {
            return {caret}null;
        }
    }
}