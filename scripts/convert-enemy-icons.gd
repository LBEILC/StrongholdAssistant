extends SceneTree

# 无头运行：参数为 enemy_types.json、上游 data 目录、输出目录。
# 来源：bingyunhe/asp-assistant-arknights 的 data/icons/enemies（PRTS、SPDatabase）。
func _init():
	var args = OS.get_cmdline_user_args()
	var types = JSON.parse_string(FileAccess.get_file_as_string(args[0]))["types"]
	var entries = {}
	for type in types.values():
		for variant in type["firstHalfVariants"] + type["secondHalfVariants"]:
			entries[variant["specialEnemyId"]] = variant["iconPath"]
	DirAccess.make_dir_recursive_absolute(args[2])
	var count = 0
	for id in entries:
		var source = args[1].path_join(entries[id])
		if not FileAccess.file_exists(source):
			push_warning("缺少原始图标：" + id)
			continue
		var image = Image.load_from_file(source)
		if image == null:
			quit(1)
			return
		# 与现有首领图标相同的尺寸和有损 WebP 格式。
		image.resize(128, 128, Image.INTERPOLATE_LANCZOS)
		if image.save_webp(args[2].path_join(id + ".webp")) != OK:
			quit(1)
			return
		count += 1
	print("已生成 %d / %d 张 128×128 WebP 图标" % [count, entries.size()])
	quit(0 if count == entries.size() else 1)
