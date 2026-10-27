/**
 * @file src/host_policy.cpp
 * @brief Definitions for the per-device host policy.
 */
// local includes
#include "host_policy.h"

// standard includes
#include <charconv>

using namespace std::literals;

namespace host_policy {

  settings_t resolve(const settings_t &global, const override_t &client) {
    return {
      client.display.value_or(global.display),
      client.mute_wavelink.value_or(global.mute_wavelink),
      client.touch_scale.value_or(global.touch_scale),
    };
  }

  std::string_view to_string(display_e display) {
    switch (display) {
      case display_e::primary:
        return "primary"sv;
      case display_e::exclusive:
        return "exclusive"sv;
      case display_e::stock:
      default:
        return "stock"sv;
    }
  }

  std::optional<display_e> display_from_string(std::string_view value) {
    if (value == "stock"sv) {
      return display_e::stock;
    }
    if (value == "primary"sv) {
      return display_e::primary;
    }
    if (value == "exclusive"sv) {
      return display_e::exclusive;
    }
    return std::nullopt;
  }

  std::string override_display_to_string(const std::optional<display_e> &display) {
    return display ? std::string {to_string(*display)} : "inherit"s;
  }

  std::optional<display_e> override_display_from_string(std::string_view value) {
    return display_from_string(value);
  }

  std::string override_bool_to_string(const std::optional<bool> &value) {
    if (!value) {
      return "inherit"s;
    }
    return *value ? "on"s : "off"s;
  }

  std::optional<bool> override_bool_from_string(std::string_view value) {
    if (value == "on"sv || value == "true"sv) {
      return true;
    }
    if (value == "off"sv || value == "false"sv) {
      return false;
    }
    return std::nullopt;
  }

  bool valid_touch_scale(int scale) {
    return scale == 0 || (scale >= 100 && scale <= 300 && scale % 25 == 0);
  }

  std::optional<int> touch_scale_from_string(std::string_view value) {
    if (value == "off"sv || value == "0"sv) {
      return 0;
    }
    int scale = 0;
    auto [end, ec] = std::from_chars(value.data(), value.data() + value.size(), scale);
    if (ec != std::errc {} || end != value.data() + value.size() || !valid_touch_scale(scale)) {
      return std::nullopt;
    }
    return scale;
  }

  std::string touch_scale_to_string(int scale) {
    return scale == 0 ? "off"s : std::to_string(scale);
  }

  std::string override_touch_scale_to_string(const std::optional<int> &scale) {
    return scale ? touch_scale_to_string(*scale) : "inherit"s;
  }

  std::optional<int> override_touch_scale_from_string(std::string_view value) {
    return touch_scale_from_string(value);
  }

}  // namespace host_policy
