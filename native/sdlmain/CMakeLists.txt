cmake_minimum_required(VERSION 3.10)
project(sdlmain C)
add_library(main SHARED main.c)
target_link_libraries(main log)
