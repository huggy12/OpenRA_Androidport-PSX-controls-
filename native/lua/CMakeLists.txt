cmake_minimum_required(VERSION 3.10)
project(lua51 C)
set(LUA_SRC ${LUA_DIR}/src)
file(GLOB SRCS ${LUA_SRC}/*.c)
list(REMOVE_ITEM SRCS ${LUA_SRC}/lua.c ${LUA_SRC}/luac.c ${LUA_SRC}/print.c)
add_library(lua51 SHARED ${SRCS})
target_compile_definitions(lua51 PRIVATE LUA_USE_POSIX LUA_USE_DLOPEN)
# Lua stores string data directly after the TString header, which bionic's FORTIFY checks
# misdetect as a buffer overrun (aborting in strchr during GC of weak tables).
target_compile_options(lua51 PRIVATE -U_FORTIFY_SOURCE -D_FORTIFY_SOURCE=0)
target_link_libraries(lua51 m dl)
