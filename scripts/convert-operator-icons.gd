extends SceneTree

# 无头运行，参数依次为原始素材缓存目录、输出目录；不打开编辑器或游戏窗口。
func _init():
	var args = OS.get_cmdline_user_args()
	var entries = JSON.parse_string(FileAccess.get_file_as_string(args[0].path_join("icons.json")))
	var count = 0
	for id in entries:
		if not FileAccess.file_exists(args[0].path_join(id + ".png")):
			push_warning("缺少原始图标：" + id)
			continue
		var image = Image.load_from_file(args[0].path_join(id + ".png"))
		var destination = args[1].path_join(id + ".webp")
		DirAccess.make_dir_recursive_absolute(destination.get_base_dir())
		if image == null or image.save_webp(destination, true) != OK:
			push_error("无法转换图标：" + id)
			quit(1)
			return
		count += 1
	print("已生成 %d / %d 张 WebP 图标" % [count, entries.size()])
	quit(0 if count == entries.size() else 1)
