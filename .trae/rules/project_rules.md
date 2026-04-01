# Unity项目规则

## 资源文件处理

### .meta文件
- **绝对不要创建或修改.meta文件**
- Unity会自动为每个资源生成.meta文件
- .meta文件包含GUID，必须由Unity自动生成

### GUID规则
- GUID是32位十六进制字符，由Unity自动生成
- **永远不要手动编造GUID**
- 如果需要引用资源，使用文件路径或让用户在Unity中设置

### 创建新资源文件
- 只创建资源文件本身（如.asset, .prefab, .cs等）
- 不要创建对应的.meta文件
- 提醒用户在Unity中刷新资源，让Unity自动生成.meta

### 复制资源文件
- 不要复制.meta文件
- 让Unity为复制的资源生成新的GUID

## 文件引用
- 引用资源时，使用相对路径：`Assets/TcgEngine/Resources/...`
- 不要在代码中硬编码GUID
- 如果需要通过GUID加载资源，使用Unity的AssetDatabase API
