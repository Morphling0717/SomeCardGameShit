// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/card.hpp"

#include <algorithm>
#include <utility>

namespace scgs {

void CardCatalog::add(CardDefinition definition) {
    if (definition.id == 0) {
        throw std::invalid_argument("card id 0 is reserved");
    }
    if (definition.cost < 0 || definition.attack < 0 || definition.health < 0) {
        throw std::invalid_argument("card numbers cannot be negative");
    }
    const auto [iterator, inserted] = definitions_.emplace(definition.id, std::move(definition));
    (void)iterator;
    if (!inserted) {
        throw std::invalid_argument("duplicate card id");
    }
}

bool CardCatalog::contains(const CardId id) const noexcept {
    return definitions_.contains(id);
}

const CardDefinition& CardCatalog::at(const CardId id) const {
    const auto iterator = definitions_.find(id);
    if (iterator == definitions_.end()) {
        throw std::out_of_range("unknown card id: " + std::to_string(id));
    }
    return iterator->second;
}

std::size_t CardCatalog::size() const noexcept {
    return definitions_.size();
}


} // namespace scgs
