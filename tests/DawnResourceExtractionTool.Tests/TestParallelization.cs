using Xunit;

// 语言是全局状态：切语言的那条测试不能和其它测试并行，否则会读到别人刚换掉的语言。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
