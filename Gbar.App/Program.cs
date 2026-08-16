using Gbar.App;

using var app = new App();
await app.InitAsync();
app.Debug();            // 调试注入点：在这里注入测试实体
app.Start();
await app.WaitForQuitAsync();
await app.QuitAsync();
