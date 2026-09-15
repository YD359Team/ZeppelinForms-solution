using System;
using System.Collections.Generic;
using System.Text;

namespace ZF_SharedLib.Models;

internal sealed record Game(
    string Title,
    string Developer,
    int Year,
    float Rating,
    bool Completed);