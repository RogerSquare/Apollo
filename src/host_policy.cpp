/**
 * @file src/host_policy.cpp
 * @brief Definitions for the per-device host policy.
 */
// local includes
#include "host_policy.h"

using namespace std::literals;

namespace host_policy {

  settings_t resolve(const settings_t &global, const override_t &client) {
    return {
      client.display.value_or(global.display),
      client.mute_wavelink.value_or(global.mute_wavelink),
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

}  // namespace host_policy
